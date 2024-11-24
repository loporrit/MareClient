using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.Shell;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop;

public class ChatChannelOverride
{
    public string ChannelName = string.Empty;
    public Action<byte[]>? ChatMessageHandler;
}

public unsafe sealed class GameChatHooks : IDisposable
{
    // Based on https://git.anna.lgbt/anna/ExtraChat/src/branch/main/client/ExtraChat/GameFunctions.cs

    private readonly ILogger<GameChatHooks> _logger;

    #region signatures
    // I do not know what kind of black magic this function performs
    [Signature("E8 ?? ?? ?? ?? 0F B7 7F 08 48 8B CE")]
    private readonly delegate* unmanaged<PronounModule*, Utf8String*, byte, Utf8String*> ProcessStringStep2;

    // Component::Shell::ShellCommandModule::ExecuteCommandInner
    private delegate void SendMessageDelegate(ShellCommandModule* module, Utf8String* message, UIModule* uiModule);
    [Signature(
        "E8 ?? ?? ?? ?? FE 86 ?? ?? ?? ?? C7 86",
        DetourName = nameof(SendMessageDetour)
    )]
    private Hook<SendMessageDelegate>? SendMessageHook { get; init; }

    // Client::UI::Shell::RaptureShellModule::SetChatChannel
    private delegate void SetChatChannelDelegate(RaptureShellModule* module, uint channel);
    [Signature(
        "E8 ?? ?? ?? ?? 33 C0 EB 1D",
        DetourName = nameof(SetChatChannelDetour)
    )]
    private Hook<SetChatChannelDelegate>? SetChatChannelHook { get; init; }

    // Component::Shell::ShellCommandModule::ExecuteCommandInner
    private delegate byte* ChangeChannelNameDelegate(AgentChatLog* agent);
    [Signature(
        "E8 ?? ?? ?? ?? BA ?? ?? ?? ?? 48 8D 4D B0 48 8B F8 E8 ?? ?? ?? ?? 41 8B D6",
        DetourName = nameof(ChangeChannelNameDetour)
    )]
    private Hook<ChangeChannelNameDelegate>? ChangeChannelNameHook { get; init; }

    // Client::UI::Agent::AgentChatLog_???
    private delegate byte ShouldDoNameLookupDelegate(AgentChatLog* agent);
    [Signature(
        "48 89 5C 24 ?? 57 48 83 EC 20 48 8B D9 40 32 FF 48 8B 49 10",
        DetourName = nameof(ShouldDoNameLookupDetour)
    )]
    private Hook<ShouldDoNameLookupDelegate>? ShouldDoNameLookupHook { get; init; }

    #endregion

    private ChatChannelOverride? _chatChannelOverride;
    private bool _shouldForceNameLookup = false;

    public ChatChannelOverride? ChatChannelOverride
    {
        get => _chatChannelOverride;
        set {
            _chatChannelOverride = value;
            this._shouldForceNameLookup = true;
        }
    }

    public GameChatHooks(ILogger<GameChatHooks> logger, IGameInteropProvider gameInteropProvider)
    {
        _logger = logger;

        logger.LogInformation("Initializing GameChatHooks");
        gameInteropProvider.InitializeFromAttributes(this);

        this.SendMessageHook?.Enable();
        this.SetChatChannelHook?.Enable();
        this.ChangeChannelNameHook?.Enable();
        this.ShouldDoNameLookupHook?.Enable();
    }

    public void Dispose()
    {
        this.SendMessageHook?.Dispose();
        this.SetChatChannelHook?.Dispose();
        this.ChangeChannelNameHook?.Dispose();
        this.ShouldDoNameLookupHook?.Dispose();
    }

    private void SendMessageDetour(ShellCommandModule* thisPtr, Utf8String* message, UIModule* uiModule)
    {
        try
        {
            var messageLength = message->Length;
            var messageSpan = message->AsSpan();

            bool isCommand = false;

            // Check if chat input begins with a command (or auto-translated command)
            if (messageLength == 0 || messageSpan[0] == (byte)'/' || !messageSpan.ContainsAnyExcept((byte)' '))
            {
                isCommand = true;
            }
            else if (messageSpan[0] == (byte)0x02) /* Payload.START_BYTE */
            {
                var payload = Payload.Decode(new BinaryReader(new UnmanagedMemoryStream(message->StringPtr, message->BufSize))) as AutoTranslatePayload;

                // Auto-translate text begins with /
                if (payload != null && payload.Text.Length > 2 && payload.Text[2] == '/')
                    isCommand = true;
            }

            // If not a command, or no override is set, then call the original chat handler
            if (isCommand || this._chatChannelOverride == null)
            {
                SendMessageHook!.OriginalDisposeSafe(thisPtr, message, uiModule);
                return;
            }

            // Otherwise, the text is to be sent to the emulated chat channel handler
            // The chat input string is rendered in to a payload for display first
            var pronounModule = UIModule.Instance()->GetPronounModule();
            var chatString1 = pronounModule->ProcessString(message, true);
            var chatString2 = this.ProcessStringStep2(pronounModule, chatString1, 1);
            var chatBytes = MemoryHelper.ReadRaw((nint)chatString2->StringPtr, chatString2->Length);

            if (chatBytes.Length > 0)
                this._chatChannelOverride.ChatMessageHandler?.Invoke(chatBytes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown during SendMessageDetour");
        }
    }

    private void SetChatChannelDetour(RaptureShellModule* module, uint channel)
    {
        try
        {
            if (this._chatChannelOverride != null)
            {
                this._chatChannelOverride = null;
                this._shouldForceNameLookup = true;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown during SetChatChannelDetour");
        }

        SetChatChannelHook!.OriginalDisposeSafe(module, channel);
    }

    private byte* ChangeChannelNameDetour(AgentChatLog* agent)
    {
        var originalResult = ChangeChannelNameHook!.OriginalDisposeSafe(agent);

        try
        {
            // Replace the chat channel name on the UI if active
            if (this._chatChannelOverride != null)
            {
                agent->ChannelLabel.SetString(this._chatChannelOverride.ChannelName);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown during ChangeChannelNameDetour");
        }

        return originalResult;
    }

    private byte ShouldDoNameLookupDetour(AgentChatLog* agent)
    {
        var originalResult = ShouldDoNameLookupHook!.OriginalDisposeSafe(agent);

        try
        {
            // Force the chat channel name to update when required
            if (this._shouldForceNameLookup)
            {
                _shouldForceNameLookup = false;
                return 1;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown during ShouldDoNameLookupDetour");
        }

        return originalResult;
    }
}
