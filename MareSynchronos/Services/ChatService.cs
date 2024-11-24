using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Data;
using MareSynchronos.Interop;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public class ChatService : DisposableMediatorSubscriberBase
{
    private readonly ILogger<ChatService> _logger;
    private readonly IChatGui _chatGui;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareConfigService _mareConfig;
    private readonly ApiController _apiController;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    private readonly Lazy<GameChatHooks> _gameChatHooks;

    public ChatService(ILogger<ChatService> logger, DalamudUtilService dalamudUtil, MareMediator mediator, ApiController apiController,
        PairManager pairManager, ILogger<GameChatHooks> logger2, IGameInteropProvider gameInteropProvider, IChatGui chatGui,
        MareConfigService mareConfig, ServerConfigurationManager serverConfigurationManager) : base(logger, mediator)
    {
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _chatGui = chatGui;
        _mareConfig = mareConfig;
        _apiController = apiController;
        _pairManager = pairManager;
        _serverConfigurationManager = serverConfigurationManager;

        Mediator.Subscribe<UserChatMsgMessage>(this, HandleUserChat);
        Mediator.Subscribe<GroupChatMsgMessage>(this, HandleGroupChat);

        _gameChatHooks = new(() => new GameChatHooks(logger2, gameInteropProvider));
    }

    protected override void Dispose(bool disposing)
    {
        if (_gameChatHooks.IsValueCreated)
            _gameChatHooks.Value!.Dispose();
    }

    private void HandleUserChat(UserChatMsgMessage message)
    {
        var chatMsg = message.ChatMsg;
        var prefix = new SeStringBuilder();
        prefix.AddText("[BnnuyChat] ");
        _chatGui.Print(new XivChatEntry{
            MessageBytes = [..prefix.Build().Encode(), ..message.ChatMsg.PayloadContent],
            Name = chatMsg.SenderName,
            Type = XivChatType.TellIncoming
        });
    }

    private void HandleGroupChat(GroupChatMsgMessage message)
    {
        if (_mareConfig.Current.DisableSyncshellChat)
            return;

        var chatMsg = message.ChatMsg;
        var shellNumber = _serverConfigurationManager.GetShellNumberForGid(message.GroupInfo.GID);
        var prefix = new SeStringBuilder();

        // TODO: Configure colors and appearance
        prefix.AddUiForeground(710);
        prefix.AddText($"[SS{shellNumber}]<");
        // TODO: Don't link to the local player because it lets you do invalid things
        prefix.Add(new PlayerPayload(chatMsg.SenderName, (uint)chatMsg.SenderHomeWorldId));
        prefix.AddText("> ");

        _chatGui.Print(new XivChatEntry{
            MessageBytes = [..prefix.Build().Encode(), ..message.ChatMsg.PayloadContent],
            Name = chatMsg.SenderName,
            Type = XivChatType.Debug
        });
    }

    // Called to update the active chat shell name if its renamed
    public void MaybeUpdateShellName(int shellNumber)
    {
        if (_mareConfig.Current.DisableSyncshellChat)
            return;

        foreach (var group in _pairManager.Groups)
        {
            if (_serverConfigurationManager.GetShellNumberForGid(group.Key.GID) == shellNumber)
            {
                if (_gameChatHooks.IsValueCreated && _gameChatHooks.Value.ChatChannelOverride != null)
                {
                    // Very dumb and won't handle re-numbering -- need to identify the active chat channel more reliably later
                    if (_gameChatHooks.Value.ChatChannelOverride.ChannelName.StartsWith($"SS [{shellNumber}]"))
                        SwitchChatShell(shellNumber);
                }
            }
        }
    }

    public void SwitchChatShell(int shellNumber)
    {
        if (_mareConfig.Current.DisableSyncshellChat)
            return;

        foreach (var group in _pairManager.Groups)
        {
            if (_serverConfigurationManager.GetShellNumberForGid(group.Key.GID) == shellNumber)
            {
                var name = _serverConfigurationManager.GetNoteForGid(group.Key.GID) ?? group.Key.AliasOrGID;
                // BUG: This doesn't always update the chat window e.g. when renaming a group
                _gameChatHooks.Value.ChatChannelOverride = new()
                {
                    ChannelName = $"SS [{shellNumber}]: {name}",
                    ChatMessageHandler = chatBytes => SendChatShell(shellNumber, chatBytes)
                };
                return;
            }
        }

        _chatGui.PrintError($"[LoporritSync] Syncshell number #{shellNumber} not found");
    }

    public void SendChatShell(int shellNumber, byte[] chatBytes)
    {
        if (_mareConfig.Current.DisableSyncshellChat)
            return;

        foreach (var group in _pairManager.Groups)
        {
            if (_serverConfigurationManager.GetShellNumberForGid(group.Key.GID) == shellNumber)
            {
                Task.Run(async () => {
                    // TODO: Cache the name and home world instead of fetching it every time
                    var chatMsg = await _dalamudUtil.RunOnFrameworkThread(() => {
                        return new ChatMessage()
                        {
                            SenderName = _dalamudUtil.GetPlayerName(),
                            SenderHomeWorldId = _dalamudUtil.GetHomeWorldId(),
                            PayloadContent = chatBytes
                        };
                    });
                    await _apiController.GroupChatSendMsg(new(group.Key), chatMsg);
                });
                return;
            }
        }

        _chatGui.PrintError($"[LoporritSync] Syncshell number #{shellNumber} not found");
    }
}