﻿using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Dto.Account;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;

namespace MareSynchronos.UI;

public class SettingsUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly FileCompactor _fileCompactor;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileUploadManager _fileTransferManager;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly FileCacheManager _fileCacheManager;
    private readonly MareCharaFileManager _mareCharaFileManager;
    private readonly PairManager _pairManager;
    private readonly ChatService _chatService;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiShared;
    private bool _deleteAccountPopupModalShown = false;
    private bool _deleteFilesPopupModalShown = false;
    private string _exportDescription = string.Empty;
    private string _lastTab = string.Empty;
    private bool? _notesSuccessfullyApplied = null;
    private bool _overwriteExistingLabels = false;
    private bool _readClearCache = false;
    private bool _readExport = false;
    private bool _wasOpen = false;
    private readonly IProgress<(int, int, FileCacheEntity)> _validationProgress;
    private Task<List<FileCacheEntity>>? _validationTask;
    private CancellationTokenSource? _validationCts;
    private (int, int, FileCacheEntity) _currentProgress;
    private Task? _exportTask;

    private bool _registrationInProgress = false;
    private bool _registrationSuccess = false;
    private string? _registrationMessage;

    public SettingsUi(ILogger<SettingsUi> logger,
        UiSharedService uiShared, MareConfigService configService,
        MareCharaFileManager mareCharaFileManager, PairManager pairManager, ChatService chatService,
        ServerConfigurationManager serverConfigurationManager,
        MareMediator mediator, PerformanceCollectorService performanceCollector,
        DalamudUtilService dalamudUtilService,
        FileUploadManager fileTransferManager,
        FileTransferOrchestrator fileTransferOrchestrator,
        FileCacheManager fileCacheManager,
        FileCompactor fileCompactor, ApiController apiController) : base(logger, mediator, "Loporrit Settings")
    {
        _configService = configService;
        _mareCharaFileManager = mareCharaFileManager;
        _pairManager = pairManager;
        _chatService = chatService;
        _serverConfigurationManager = serverConfigurationManager;
        _performanceCollector = performanceCollector;
        _dalamudUtilService = dalamudUtilService;
        _fileTransferManager = fileTransferManager;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCacheManager = fileCacheManager;
        _apiController = apiController;
        _fileCompactor = fileCompactor;
        _uiShared = uiShared;
        AllowClickthrough = false;
        AllowPinning = false;
        _validationProgress = new Progress<(int, int, FileCacheEntity)>(v => _currentProgress = v);

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(600, 400),
            MaximumSize = new Vector2(600, 2000),
        };

        Mediator.Subscribe<OpenSettingsUiMessage>(this, (_) => Toggle());
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiSharedService_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiSharedService_GposeEnd());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) => LastCreatedCharacterData = msg.CharacterData);
        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => _currentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));
    }

    public CharacterData? LastCreatedCharacterData { private get; set; }
    private ApiController ApiController => _uiShared.ApiController;

    public override void Draw()
    {
        _ = _uiShared.DrawOtherPluginState();

        DrawSettingsContent();
    }

    public override void OnClose()
    {
        _uiShared.EditTrackerPosition = false;
        base.OnClose();
    }

    private void DrawBlockedTransfers()
    {
        _lastTab = "BlockedTransfers";
        UiSharedService.ColorTextWrapped("Files that you attempted to upload or download that were forbidden to be transferred by their creators will appear here. " +
                             "If you see file paths from your drive here, then those files were not allowed to be uploaded. If you see hashes, those files were not allowed to be downloaded. " +
                             "Ask your paired friend to send you the mod in question through other means or acquire the mod yourself.",
            ImGuiColors.DalamudGrey);

        if (ImGui.BeginTable("TransfersTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(
                $"Hash/Filename");
            ImGui.TableSetupColumn($"Forbidden by");

            ImGui.TableHeadersRow();

            foreach (var item in _fileTransferOrchestrator.ForbiddenTransfers)
            {
                ImGui.TableNextColumn();
                if (item is UploadFileTransfer transfer)
                {
                    ImGui.TextUnformatted(transfer.LocalFile);
                }
                else
                {
                    ImGui.TextUnformatted(item.Hash);
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(item.ForbiddenBy);
            }
            ImGui.EndTable();
        }
    }

    private void DrawCurrentTransfers()
    {
        _lastTab = "Transfers";
        UiSharedService.FontText("Transfer Settings", _uiShared.UidFont);

        int maxParallelDownloads = _configService.Current.ParallelDownloads;
        bool useAlternativeUpload = _configService.Current.UseAlternativeFileUpload;
        int downloadSpeedLimit = _configService.Current.DownloadSpeedLimitInBytes;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Global Download Speed Limit");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("###speedlimit", ref downloadSpeedLimit))
        {
            _configService.Current.DownloadSpeedLimitInBytes = downloadSpeedLimit;
            _configService.Save();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        _uiShared.DrawCombo("###speed", new[] { DownloadSpeeds.Bps, DownloadSpeeds.KBps, DownloadSpeeds.MBps },
            (s) => s switch
            {
                DownloadSpeeds.Bps => "Byte/s",
                DownloadSpeeds.KBps => "KB/s",
                DownloadSpeeds.MBps => "MB/s",
                _ => throw new NotSupportedException()
            }, (s) =>
            {
                _configService.Current.DownloadSpeedType = s;
                _configService.Save();
                Mediator.Publish(new DownloadLimitChangedMessage());
            }, _configService.Current.DownloadSpeedType);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("0 = No limit/infinite");

        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Maximum Parallel Downloads", ref maxParallelDownloads, 1, 10))
        {
            _configService.Current.ParallelDownloads = maxParallelDownloads;
            _configService.Save();
        }

        if (ImGui.Checkbox("Use Alternative Upload Method", ref useAlternativeUpload))
        {
            _configService.Current.UseAlternativeFileUpload = useAlternativeUpload;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will attempt to upload files in one go instead of a stream. Typically not necessary to enable. Use if you have upload issues.");

        ImGui.Separator();
        UiSharedService.FontText("Transfer UI", _uiShared.UidFont);

        bool showTransferWindow = _configService.Current.ShowTransferWindow;
        if (ImGui.Checkbox("Show separate transfer window", ref showTransferWindow))
        {
            _configService.Current.ShowTransferWindow = showTransferWindow;
            _configService.Save();
        }
        UiSharedService.DrawHelpText($"The download window will show the current progress of outstanding downloads.{Environment.NewLine}{Environment.NewLine}" +
            $"What do W/Q/P/D stand for?{Environment.NewLine}W = Waiting for Slot (see Maximum Parallel Downloads){Environment.NewLine}" +
            $"Q = Queued on Server, waiting for queue ready signal{Environment.NewLine}" +
            $"P = Processing download (aka downloading){Environment.NewLine}" +
            $"D = Decompressing download");
        if (!_configService.Current.ShowTransferWindow) ImGui.BeginDisabled();
        ImGui.Indent();
        bool editTransferWindowPosition = _uiShared.EditTrackerPosition;
        if (ImGui.Checkbox("Edit Transfer Window position", ref editTransferWindowPosition))
        {
            _uiShared.EditTrackerPosition = editTransferWindowPosition;
        }
        ImGui.Unindent();
        if (!_configService.Current.ShowTransferWindow) ImGui.EndDisabled();

        bool showTransferBars = _configService.Current.ShowTransferBars;
        if (ImGui.Checkbox("Show transfer bars rendered below players", ref showTransferBars))
        {
            _configService.Current.ShowTransferBars = showTransferBars;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will render a progress bar during the download at the feet of the player you are downloading from.");

        if (!showTransferBars) ImGui.BeginDisabled();
        ImGui.Indent();
        bool transferBarShowText = _configService.Current.TransferBarsShowText;
        if (ImGui.Checkbox("Show Download Text", ref transferBarShowText))
        {
            _configService.Current.TransferBarsShowText = transferBarShowText;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Shows download text (amount of MiB downloaded) in the transfer bars");
        int transferBarWidth = _configService.Current.TransferBarsWidth;
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Transfer Bar Width", ref transferBarWidth, 0, 500))
        {
            if (transferBarWidth < 10)
                transferBarWidth = 10;
            _configService.Current.TransferBarsWidth = transferBarWidth;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Width of the displayed transfer bars (will never be less wide than the displayed text)");
        int transferBarHeight = _configService.Current.TransferBarsHeight;
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Transfer Bar Height", ref transferBarHeight, 0, 50))
        {
            if (transferBarHeight < 2)
                transferBarHeight = 2;
            _configService.Current.TransferBarsHeight = transferBarHeight;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Height of the displayed transfer bars (will never be less tall than the displayed text)");
        bool showUploading = _configService.Current.ShowUploading;
        if (ImGui.Checkbox("Show 'Uploading' text below players that are currently uploading", ref showUploading))
        {
            _configService.Current.ShowUploading = showUploading;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will render an 'Uploading' text at the feet of the player that is in progress of uploading data.");

        ImGui.Unindent();
        if (!showUploading) ImGui.BeginDisabled();
        ImGui.Indent();
        bool showUploadingBigText = _configService.Current.ShowUploadingBigText;
        if (ImGui.Checkbox("Large font for 'Uploading' text", ref showUploadingBigText))
        {
            _configService.Current.ShowUploadingBigText = showUploadingBigText;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will render an 'Uploading' text in a larger font.");

        ImGui.Unindent();

        if (!showUploading) ImGui.EndDisabled();
        if (!showTransferBars) ImGui.EndDisabled();

        ImGui.Separator();
        UiSharedService.FontText("Current Transfers", _uiShared.UidFont);

        if (ImGui.BeginTabBar("TransfersTabBar"))
        {
            if (ApiController.ServerState is ServerState.Connected && ImGui.BeginTabItem("Transfers"))
            {
                ImGui.TextUnformatted("Uploads");
                if (ImGui.BeginTable("UploadsTable", 3))
                {
                    ImGui.TableSetupColumn("File");
                    ImGui.TableSetupColumn("Uploaded");
                    ImGui.TableSetupColumn("Size");
                    ImGui.TableHeadersRow();
                    foreach (var transfer in _fileTransferManager.CurrentUploads.ToArray())
                    {
                        var color = UiSharedService.UploadColor((transfer.Transferred, transfer.Total));
                        var col = ImRaii.PushColor(ImGuiCol.Text, color);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(transfer.Hash);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(UiSharedService.ByteToString(transfer.Transferred));
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(UiSharedService.ByteToString(transfer.Total));
                        col.Dispose();
                        ImGui.TableNextRow();
                    }

                    ImGui.EndTable();
                }
                ImGui.Separator();
                ImGui.TextUnformatted("Downloads");
                if (ImGui.BeginTable("DownloadsTable", 4))
                {
                    ImGui.TableSetupColumn("User");
                    ImGui.TableSetupColumn("Server");
                    ImGui.TableSetupColumn("Files");
                    ImGui.TableSetupColumn("Download");
                    ImGui.TableHeadersRow();

                    foreach (var transfer in _currentDownloads.ToArray())
                    {
                        var userName = transfer.Key.Name;
                        foreach (var entry in transfer.Value)
                        {
                            var color = UiSharedService.UploadColor((entry.Value.TransferredBytes, entry.Value.TotalBytes));
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(userName);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(entry.Key);
                            var col = ImRaii.PushColor(ImGuiCol.Text, color);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(entry.Value.TransferredFiles + "/" + entry.Value.TotalFiles);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(UiSharedService.ByteToString(entry.Value.TransferredBytes) + "/" + UiSharedService.ByteToString(entry.Value.TotalBytes));
                            ImGui.TableNextColumn();
                            col.Dispose();
                            ImGui.TableNextRow();
                        }
                    }

                    ImGui.EndTable();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Blocked Transfers"))
            {
                DrawBlockedTransfers();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private static readonly List<(XivChatType, string)> _syncshellChatTypes = [
        (0, "(use global setting)"),
        (XivChatType.Debug, "Debug"),
        (XivChatType.Echo, "Echo"),
        (XivChatType.StandardEmote, "Standard Emote"),
        (XivChatType.CustomEmote, "Custom Emote"),
        (XivChatType.SystemMessage, "System Message"),
        (XivChatType.SystemError, "System Error"),
        (XivChatType.GatheringSystemMessage, "Gathering Message"),
        (XivChatType.ErrorMessage, "Error message"),
    ];

    private void DrawChatConfig()
    {
        _lastTab = "Chat";

        UiSharedService.FontText("Chat Settings", _uiShared.UidFont);

        var disableSyncshellChat = _configService.Current.DisableSyncshellChat;

        if (ImGui.Checkbox("Disable chat globally", ref disableSyncshellChat))
        {
            _configService.Current.DisableSyncshellChat = disableSyncshellChat;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Global setting to disable chat for all syncshells.");

        using var pushDisableGlobal = ImRaii.Disabled(disableSyncshellChat);

        var uiColors = _dalamudUtilService.UiColors.Value;
        int globalChatColor = _configService.Current.ChatColor;

        if (globalChatColor != 0 && !uiColors.ContainsKey(globalChatColor))
        {
            globalChatColor = 0;
            _configService.Current.ChatColor = 0;
            _configService.Save();
        }

        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        _uiShared.DrawColorCombo("Chat text color", Enumerable.Concat([0], uiColors.Keys),
        i => i switch
        {
            0 => (uiColors[ChatService.DefaultColor].UIForeground, "Plugin Default"),
            _ => (uiColors[i].UIForeground, $"[{i}] Sample Text")
        },
        i => {
            _configService.Current.ChatColor = i;
            _configService.Save();
        }, globalChatColor);

        int globalChatType = _configService.Current.ChatLogKind;
        int globalChatTypeIdx = _syncshellChatTypes.FindIndex(x => globalChatType == (int)x.Item1);

        if (globalChatTypeIdx == -1)
            globalChatTypeIdx = 0;

        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        _uiShared.DrawCombo("Chat channel", Enumerable.Range(1, _syncshellChatTypes.Count - 1), i => $"{_syncshellChatTypes[i].Item2}",
        i => {
            if (_configService.Current.ChatLogKind == (int)_syncshellChatTypes[i].Item1)
                return;
            _configService.Current.ChatLogKind = (int)_syncshellChatTypes[i].Item1;
            _chatService.PrintChannelExample($"Selected channel: {_syncshellChatTypes[i].Item2}");
            _configService.Save();
        }, globalChatTypeIdx);
        UiSharedService.DrawHelpText("FFXIV chat channel to output chat messages on.");

        ImGui.SetWindowFontScale(0.6f);
        UiSharedService.FontText("\"Chat 2\" Plugin Integration", _uiShared.UidFont);
        ImGui.SetWindowFontScale(1.0f);

        // TODO: ExtraChat API impersonation
        /*
        var extraChatAPI = _configService.Current.ExtraChatAPI;
        if (ImGui.Checkbox("ExtraChat replacement mode", ref extraChatAPI))
        {
            _configService.Current.ExtraChatAPI = extraChatAPI;
            if (extraChatAPI)
                _configService.Current.ExtraChatTags = true;
            _configService.Save();
        }
        ImGui.EndDisabled();
        UiSharedService.DrawHelpText("Enable ExtraChat APIs for full Chat 2 plugin integration.\n\nDo not enable this if ExtraChat is also installed and running.");
        */

        var extraChatTags = _configService.Current.ExtraChatTags;
        if (ImGui.Checkbox("Tag messages as ExtraChat", ref extraChatTags))
        {
            _configService.Current.ExtraChatTags = extraChatTags;
            if (!extraChatTags)
                _configService.Current.ExtraChatAPI = false;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("If enabled, messages will be filtered under the category \"ExtraChat channels: All\".\n\nThis works even if ExtraChat is also installed and enabled.");

        ImGui.Separator();

        UiSharedService.FontText("Syncshell Settings", _uiShared.UidFont);

        if (!ApiController.ServerAlive)
        {
            ImGui.Text("Connect to the server to configure individual syncshell settings.");
            return;
        }

        foreach (var group in _pairManager.Groups.OrderBy(k => k.Key.GID))
        {
            var gid = group.Key.GID;
            using var pushId = ImRaii.PushId(gid);

            var shellConfig = _serverConfigurationManager.GetShellConfigForGid(gid);
            var shellNumber = shellConfig.ShellNumber;
            var shellEnabled = shellConfig.Enabled;
            var shellName = _serverConfigurationManager.GetNoteForGid(gid) ?? group.Key.AliasOrGID;

            if (shellEnabled)
                shellName = $"[{shellNumber}] {shellName}";

            ImGui.SetWindowFontScale(0.6f);
            UiSharedService.FontText(shellName, _uiShared.UidFont);
            ImGui.SetWindowFontScale(1.0f);

            using var pushIndent = ImRaii.PushIndent();

            if (ImGui.Checkbox($"Enable chat for this syncshell##{gid}", ref shellEnabled))
            {
                // If there is an active group with the same syncshell number, pick a new one
                int nextNumber = 1;
                bool conflict = false;
                foreach (var otherGroup in _pairManager.Groups)
                {
                    if (gid == otherGroup.Key.GID) continue;
                    var otherShellConfig = _serverConfigurationManager.GetShellConfigForGid(otherGroup.Key.GID);
                    if (otherShellConfig.Enabled && otherShellConfig.ShellNumber == shellNumber)
                        conflict = true;
                    nextNumber = System.Math.Max(nextNumber, otherShellConfig.ShellNumber) + 1;
                }
                if (conflict)
                    shellConfig.ShellNumber = nextNumber;
                shellConfig.Enabled = shellEnabled;
                _serverConfigurationManager.SaveShellConfigForGid(gid, shellConfig);
            }

            using var pushDisabled = ImRaii.Disabled(!shellEnabled);

            ImGui.SetNextItemWidth(50 * ImGuiHelpers.GlobalScale);

            var setSyncshellNumberFn = (int i) => {
                // Find an active group with the same syncshell number as selected, and swap it
                // This logic can leave duplicate IDs present in the config but its not critical
                foreach (var otherGroup in _pairManager.Groups)
                {
                    if (gid == otherGroup.Key.GID) continue;
                    var otherShellConfig = _serverConfigurationManager.GetShellConfigForGid(otherGroup.Key.GID);
                    if (otherShellConfig.Enabled && otherShellConfig.ShellNumber == i)
                    {
                        otherShellConfig.ShellNumber = shellNumber;
                        _serverConfigurationManager.SaveShellConfigForGid(otherGroup.Key.GID, otherShellConfig);
                        break;
                    }
                }
                shellConfig.ShellNumber = i;
                _serverConfigurationManager.SaveShellConfigForGid(gid, shellConfig);
            };

            // _uiShared.DrawCombo() remembers the selected option -- we don't want that, because the value can change
            if (ImGui.BeginCombo("Syncshell number##{gid}", $"{shellNumber}"))
            {
                // Same hard-coded number in CommandManagerService
                for (int i = 1; i <= ChatService.CommandMaxNumber; ++i)
                {
                    if (ImGui.Selectable($"{i}", i == shellNumber))
                    {
                        // Find an active group with the same syncshell number as selected, and swap it
                        // This logic can leave duplicate IDs present in the config but its not critical
                        foreach (var otherGroup in _pairManager.Groups)
                        {
                            if (gid == otherGroup.Key.GID) continue;
                            var otherShellConfig = _serverConfigurationManager.GetShellConfigForGid(otherGroup.Key.GID);
                            if (otherShellConfig.Enabled && otherShellConfig.ShellNumber == i)
                            {
                                otherShellConfig.ShellNumber = shellNumber;
                                _serverConfigurationManager.SaveShellConfigForGid(otherGroup.Key.GID, otherShellConfig);
                                break;
                            }
                        }
                        shellConfig.ShellNumber = i;
                        _serverConfigurationManager.SaveShellConfigForGid(gid, shellConfig);
                    }
                }
                ImGui.EndCombo();
            }

            if (shellConfig.Color != 0 && !uiColors.ContainsKey(shellConfig.Color))
            {
                shellConfig.Color = 0;
                _serverConfigurationManager.SaveShellConfigForGid(gid, shellConfig);
            }

            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            _uiShared.DrawColorCombo($"Chat text color##{gid}", Enumerable.Concat([0], uiColors.Keys),
            i => i switch
            {
                0 => (uiColors[globalChatColor > 0 ? globalChatColor : ChatService.DefaultColor].UIForeground, "(use global setting)"),
                _ => (uiColors[i].UIForeground, $"[{i}] Sample Text")
            },
            i => {
                shellConfig.Color = i;
                _serverConfigurationManager.SaveShellConfigForGid(gid, shellConfig);
            }, shellConfig.Color);

            int shellChatTypeIdx = _syncshellChatTypes.FindIndex(x => shellConfig.LogKind == (int)x.Item1);

            if (shellChatTypeIdx == -1)
                shellChatTypeIdx = 0;

            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            _uiShared.DrawCombo($"Chat channel##{gid}", Enumerable.Range(0, _syncshellChatTypes.Count), i => $"{_syncshellChatTypes[i].Item2}",
            i => {
                shellConfig.LogKind = (int)_syncshellChatTypes[i].Item1;
                _serverConfigurationManager.SaveShellConfigForGid(gid, shellConfig);
            }, shellChatTypeIdx);
            UiSharedService.DrawHelpText("Override the FFXIV chat channel used for this syncshell.");
        }
    }

    private void DrawDebug()
    {
        _lastTab = "Debug";

        UiSharedService.FontText("Debug", _uiShared.UidFont);
#if DEBUG
        if (LastCreatedCharacterData != null && ImGui.TreeNode("Last created character data"))
        {
            foreach (var l in JsonSerializer.Serialize(LastCreatedCharacterData, new JsonSerializerOptions() { WriteIndented = true }).Split('\n'))
            {
                ImGui.TextUnformatted($"{l}");
            }

            ImGui.TreePop();
        }
#endif
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Copy, "[DEBUG] Copy Last created Character Data to clipboard"))
        {
            if (LastCreatedCharacterData != null)
            {
                ImGui.SetClipboardText(JsonSerializer.Serialize(LastCreatedCharacterData, new JsonSerializerOptions() { WriteIndented = true }));
            }
            else
            {
                ImGui.SetClipboardText("ERROR: No created character data, cannot copy.");
            }
        }
        UiSharedService.AttachToolTip("Use this when reporting mods being rejected from the server.");

        _uiShared.DrawCombo("Log Level", Enum.GetValues<LogLevel>(), (l) => l.ToString(), (l) =>
        {
            _configService.Current.LogLevel = l;
            _configService.Save();
        }, _configService.Current.LogLevel);

        bool logPerformance = _configService.Current.LogPerformance;
        if (ImGui.Checkbox("Log Performance Counters", ref logPerformance))
        {
            _configService.Current.LogPerformance = logPerformance;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Enabling this can incur a (slight) performance impact. Enabling this for extended periods of time is not recommended.");

        using var disabled = ImRaii.Disabled(!logPerformance);
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.StickyNote, "Print Performance Stats to /xllog"))
        {
            _performanceCollector.PrintPerformanceStats();
        }
        ImGui.SameLine();
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.StickyNote, "Print Performance Stats (last 60s) to /xllog"))
        {
            _performanceCollector.PrintPerformanceStats(60);
        }
    }

    private void DrawFileStorageSettings()
    {
        _lastTab = "FileCache";

        UiSharedService.FontText("Export MCDF", _uiShared.UidFont);

        UiSharedService.TextWrapped("This feature allows you to pack your character appearance into a MCDF file and manually send it to other people. MCDF files can imported during GPose.");

        ImGui.Checkbox("##readExport", ref _readExport);
        ImGui.SameLine();
        UiSharedService.TextWrapped("I understand that by exporting my character data and sending it to other people I am giving away my current character appearance irrevocably. People I am sharing my data with have the ability to share it with other people without limitations.");

        if (_readExport)
        {
            ImGui.Indent();

            if (!_mareCharaFileManager.CurrentlyWorking)
            {
                ImGui.InputTextWithHint("Export Descriptor", "This description will be shown on loading the data", ref _exportDescription, 255);
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Save, "Export Character as MCDF"))
                {
                    string defaultFileName = string.IsNullOrEmpty(_exportDescription)
                        ? "export.mcdf"
                        : string.Join('_', $"{_exportDescription}.mcdf".Split(Path.GetInvalidFileNameChars()));
                    _uiShared.FileDialogManager.SaveFileDialog("Export Character to file", ".mcdf", defaultFileName, ".mcdf", (success, path) =>
                    {
                        if (!success) return;

                        _configService.Current.ExportFolder = Path.GetDirectoryName(path) ?? string.Empty;
                        _configService.Save();

                        _exportTask = Task.Run(() =>
                        {
                            var desc = _exportDescription;
                            _exportDescription = string.Empty;
                            _mareCharaFileManager.SaveMareCharaFile(LastCreatedCharacterData, desc, path);
                        });
                    }, Directory.Exists(_configService.Current.ExportFolder) ? _configService.Current.ExportFolder : null);
                }
                UiSharedService.ColorTextWrapped("Note: For best results make sure you have everything you want to be shared as well as the correct character appearance" +
                    " equipped and redraw your character before exporting.", ImGuiColors.DalamudYellow);
            }
            else
            {
                UiSharedService.ColorTextWrapped("Export in progress", ImGuiColors.DalamudYellow);
            }

            if (_exportTask?.IsFaulted ?? false)
            {
                UiSharedService.ColorTextWrapped("Export failed, check /xllog for more details.", ImGuiColors.DalamudRed);
            }

            ImGui.Unindent();
        }
        bool openInGpose = _configService.Current.OpenGposeImportOnGposeStart;
        if (ImGui.Checkbox("Open MCDF import window when GPose loads", ref openInGpose))
        {
            _configService.Current.OpenGposeImportOnGposeStart = openInGpose;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will automatically open the import menu when loading into Gpose. If unchecked you can open the menu manually with /sync gpose");

        ImGui.Separator();

        UiSharedService.FontText("Storage", _uiShared.UidFont);

        UiSharedService.TextWrapped("Loporrit stores downloaded files from paired people permanently. This is to improve loading performance and requiring less downloads. " +
            "The storage governs itself by clearing data beyond the set storage size. Please set the storage size accordingly. It is not necessary to manually clear the storage.");

        _uiShared.DrawFileScanState();
        _uiShared.DrawTimeSpanBetweenScansSetting();
        _uiShared.DrawCacheDirectorySetting();
        ImGui.TextUnformatted($"Currently utilized local storage: {UiSharedService.ByteToString(_uiShared.FileCacheSize)}");
        bool isLinux = Util.IsWine();
        if (isLinux) ImGui.BeginDisabled();
        bool useFileCompactor = _configService.Current.UseCompactor;
        if (ImGui.Checkbox("Use file compactor", ref useFileCompactor))
        {
            _configService.Current.UseCompactor = useFileCompactor;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("The file compactor can massively reduce your saved files. It might incur a minor penalty on loading files on a slow CPU." + Environment.NewLine
            + "It is recommended to leave it enabled to save on space.");

        if (!_fileCompactor.MassCompactRunning)
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.FileArchive, "Compact all files in storage"))
            {
                _ = Task.Run(() => _fileCompactor.CompactStorage(compress: true));
            }
            UiSharedService.AttachToolTip("This will run compression on all files in your current storage folder." + Environment.NewLine
                + "You do not need to run this manually if you keep the file compactor enabled.");
            ImGui.SameLine();
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.File, "Decompact all files in storage"))
            {
                _ = Task.Run(() => _fileCompactor.CompactStorage(compress: false));
            }
            UiSharedService.AttachToolTip("This will run decompression on all files in your current storage folder.");
        }
        else
        {
            UiSharedService.ColorText($"File compactor currently running ({_fileCompactor.Progress})", ImGuiColors.DalamudYellow);
        }
        if (isLinux)
        {
            ImGui.EndDisabled();
            ImGui.TextUnformatted("The file compactor is only available on Windows.");
        }
        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));

        ImGui.Separator();
        UiSharedService.TextWrapped("File Storage validation can make sure that all files in your local storage folder are valid. " +
            "Run the validation before you clear the Storage for no reason. " + Environment.NewLine +
            "This operation, depending on how many files you have in your storage, can take a while and will be CPU and drive intensive.");
        using (ImRaii.Disabled(_validationTask != null && !_validationTask.IsCompleted))
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Check, "Start File Storage Validation"))
            {
                _validationCts?.Cancel();
                _validationCts?.Dispose();
                _validationCts = new();
                var token = _validationCts.Token;
                _validationTask = Task.Run(() => _fileCacheManager.ValidateLocalIntegrity(_validationProgress, token));
            }
        }
        if (_validationTask != null && !_validationTask.IsCompleted)
        {
            ImGui.SameLine();
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Times, "Cancel"))
            {
                _validationCts?.Cancel();
            }
        }

        if (_validationTask != null)
        {
            using (ImRaii.PushIndent(20f))
            {
                if (_validationTask.IsCompleted)
                {
                    UiSharedService.TextWrapped($"The storage validation has completed and removed {_validationTask.Result.Count} invalid files from storage.");
                }
                else
                {

                    UiSharedService.TextWrapped($"Storage validation is running: {_currentProgress.Item1}/{_currentProgress.Item2}");
                    UiSharedService.TextWrapped($"Current item: {_currentProgress.Item3.ResolvedFilepath}");
                }
            }
        }
        ImGui.Separator();

        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));
        ImGui.TextUnformatted("To clear the local storage accept the following disclaimer");
        ImGui.Indent();
        ImGui.Checkbox("##readClearCache", ref _readClearCache);
        ImGui.SameLine();
        UiSharedService.TextWrapped("I understand that: " + Environment.NewLine + "- By clearing the local storage I put the file servers of my connected service under extra strain by having to redownload all data."
            + Environment.NewLine + "- This is not a step to try to fix sync issues."
            + Environment.NewLine + "- This can make the situation of not getting other players data worse in situations of heavy file server load.");
        if (!_readClearCache)
            ImGui.BeginDisabled();
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Clear local storage") && UiSharedService.CtrlPressed() && _readClearCache)
        {
            _ = Task.Run(() =>
            {
                foreach (var file in Directory.GetFiles(_configService.Current.CacheFolder))
                {
                    File.Delete(file);
                }

                _uiShared.RecalculateFileCacheSize();
            });
        }
        UiSharedService.AttachToolTip("You normally do not need to do this. THIS IS NOT SOMETHING YOU SHOULD BE DOING TO TRY TO FIX SYNC ISSUES." + Environment.NewLine
            + "This will solely remove all downloaded data from all players and will require you to re-download everything again." + Environment.NewLine
            + "Loporrit's storage is self-clearing and will not surpass the limit you have set it to." + Environment.NewLine
            + "If you still think you need to do this hold CTRL while pressing the button.");
        if (!_readClearCache)
            ImGui.EndDisabled();
        ImGui.Unindent();
    }

    private void DrawGeneral()
    {
        if (!string.Equals(_lastTab, "General", StringComparison.OrdinalIgnoreCase))
        {
            _notesSuccessfullyApplied = null;
        }

        _lastTab = "General";
        //UiSharedService.FontText("Experimental", _uiShared.UidFont);
        //ImGui.Separator();

        UiSharedService.FontText("Notes", _uiShared.UidFont);
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.StickyNote, "Export all your user notes to clipboard"))
        {
            ImGui.SetClipboardText(UiSharedService.GetNotes(_pairManager.DirectPairs.UnionBy(_pairManager.GroupPairs.SelectMany(p => p.Value), p => p.UserData, UserDataComparer.Instance).ToList()));
        }
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.FileImport, "Import notes from clipboard"))
        {
            _notesSuccessfullyApplied = null;
            var notes = ImGui.GetClipboardText();
            _notesSuccessfullyApplied = _uiShared.ApplyNotesFromClipboard(notes, _overwriteExistingLabels);
        }

        ImGui.SameLine();
        ImGui.Checkbox("Overwrite existing notes", ref _overwriteExistingLabels);
        UiSharedService.DrawHelpText("If this option is selected all already existing notes for UIDs will be overwritten by the imported notes.");
        if (_notesSuccessfullyApplied.HasValue && _notesSuccessfullyApplied.Value)
        {
            UiSharedService.ColorTextWrapped("User Notes successfully imported", ImGuiColors.HealerGreen);
        }
        else if (_notesSuccessfullyApplied.HasValue && !_notesSuccessfullyApplied.Value)
        {
            UiSharedService.ColorTextWrapped("Attempt to import notes from clipboard failed. Check formatting and try again", ImGuiColors.DalamudRed);
        }

        var openPopupOnAddition = _configService.Current.OpenPopupOnAdd;

        if (ImGui.Checkbox("Open Notes Popup on user addition", ref openPopupOnAddition))
        {
            _configService.Current.OpenPopupOnAdd = openPopupOnAddition;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will open a popup that allows you to set the notes for a user after successfully adding them to your individual pairs.");

        ImGui.Separator();
        UiSharedService.FontText("UI", _uiShared.UidFont);
        var showCharacterNames = _configService.Current.ShowCharacterNames;
        var showVisibleSeparate = _configService.Current.ShowVisibleUsersSeparately;
        var showOfflineSeparate = _configService.Current.ShowOfflineUsersSeparately;
        var showProfiles = _configService.Current.ProfilesShow;
        var showNsfwProfiles = _configService.Current.ProfilesAllowNsfw;
        var profileDelay = _configService.Current.ProfileDelay;
        var profileOnRight = _configService.Current.ProfilePopoutRight;
        var enableRightClickMenu = _configService.Current.EnableRightClickMenus;
        var enableDtrEntry = _configService.Current.EnableDtrEntry;
        var showUidInDtrTooltip = _configService.Current.ShowUidInDtrTooltip;
        var preferNoteInDtrTooltip = _configService.Current.PreferNoteInDtrTooltip;

        if (ImGui.Checkbox("Enable Game Right Click Menu Entries", ref enableRightClickMenu))
        {
            _configService.Current.EnableRightClickMenus = enableRightClickMenu;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will add Loporrit related right click menu entries in the game UI on paired players.");

        if (ImGui.Checkbox("Display status and visible pair count in Server Info Bar", ref enableDtrEntry))
        {
            _configService.Current.EnableDtrEntry = enableDtrEntry;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will add Loporrit connection status and visible pair count in the Server Info Bar.\nYou can further configure this through your Dalamud Settings.");

        using (ImRaii.Disabled(!enableDtrEntry))
        {
            using var indent = ImRaii.PushIndent();
            if (ImGui.Checkbox("Show visible character's UID in tooltip", ref showUidInDtrTooltip))
            {
                _configService.Current.ShowUidInDtrTooltip = showUidInDtrTooltip;
                _configService.Save();
            }

            if (ImGui.Checkbox("Prefer notes over player names in tooltip", ref preferNoteInDtrTooltip))
            {
                _configService.Current.PreferNoteInDtrTooltip = preferNoteInDtrTooltip;
                _configService.Save();
            }

            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            _uiShared.DrawCombo("Server Info Bar style", Enumerable.Range(0, DtrEntry.NumStyles), (i) => DtrEntry.RenderDtrStyle(i, "123"),
            (i) =>
            {
                _configService.Current.DtrStyle = i;
                _configService.Save();
            }, _configService.Current.DtrStyle);
        }

        if (ImGui.Checkbox("Show separate Visible group", ref showVisibleSeparate))
        {
            _configService.Current.ShowVisibleUsersSeparately = showVisibleSeparate;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will show all currently visible users in a special 'Visible' group in the main UI.");

        if (ImGui.Checkbox("Show separate Offline group", ref showOfflineSeparate))
        {
            _configService.Current.ShowOfflineUsersSeparately = showOfflineSeparate;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will show all currently offline users in a special 'Offline' group in the main UI.");

        if (ImGui.Checkbox("Show player names", ref showCharacterNames))
        {
            _configService.Current.ShowCharacterNames = showCharacterNames;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will show character names instead of UIDs when possible");

        if (ImGui.Checkbox("Show Profiles on Hover", ref showProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesShow = showProfiles;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will show the configured user profile after a set delay");
        ImGui.Indent();
        if (!showProfiles) ImGui.BeginDisabled();
        if (ImGui.Checkbox("Popout profiles on the right", ref profileOnRight))
        {
            _configService.Current.ProfilePopoutRight = profileOnRight;
            _configService.Save();
            Mediator.Publish(new CompactUiChange(Vector2.Zero, Vector2.Zero));
        }
        UiSharedService.DrawHelpText("Will show profiles on the right side of the main UI");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Hover Delay", ref profileDelay, 1, 10))
        {
            _configService.Current.ProfileDelay = profileDelay;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Delay until the profile should be displayed");
        if (!showProfiles) ImGui.EndDisabled();
        ImGui.Unindent();
        if (ImGui.Checkbox("Show profiles marked as NSFW", ref showNsfwProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesAllowNsfw = showNsfwProfiles;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Will show profiles that have the NSFW tag enabled");

        ImGui.Separator();

        var disableOptionalPluginWarnings = _configService.Current.DisableOptionalPluginWarnings;
        var onlineNotifs = _configService.Current.ShowOnlineNotifications;
        var onlineNotifsPairsOnly = _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs;
        var onlineNotifsNamedOnly = _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs;
        UiSharedService.FontText("Notifications", _uiShared.UidFont);

        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        _uiShared.DrawCombo("Info Notification Display##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.InfoNotification = i;
            _configService.Save();
        }, _configService.Current.InfoNotification);
        UiSharedService.DrawHelpText("The location where \"Info\" notifications will display."
                      + Environment.NewLine + "'Nowhere' will not show any Info notifications"
                      + Environment.NewLine + "'Chat' will print Info notifications in chat"
                      + Environment.NewLine + "'Toast' will show Warning toast notifications in the bottom right corner"
                      + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        _uiShared.DrawCombo("Warning Notification Display##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.WarningNotification = i;
            _configService.Save();
        }, _configService.Current.WarningNotification);
        UiSharedService.DrawHelpText("The location where \"Warning\" notifications will display."
                              + Environment.NewLine + "'Nowhere' will not show any Warning notifications"
                              + Environment.NewLine + "'Chat' will print Warning notifications in chat"
                              + Environment.NewLine + "'Toast' will show Warning toast notifications in the bottom right corner"
                              + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        _uiShared.DrawCombo("Error Notification Display##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.ErrorNotification = i;
            _configService.Save();
        }, _configService.Current.ErrorNotification);
        UiSharedService.DrawHelpText("The location where \"Error\" notifications will display."
                              + Environment.NewLine + "'Nowhere' will not show any Error notifications"
                              + Environment.NewLine + "'Chat' will print Error notifications in chat"
                              + Environment.NewLine + "'Toast' will show Error toast notifications in the bottom right corner"
                              + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        if (ImGui.Checkbox("Disable optional plugin warnings", ref disableOptionalPluginWarnings))
        {
            _configService.Current.DisableOptionalPluginWarnings = disableOptionalPluginWarnings;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Enabling this will not show any \"Warning\" labeled messages for missing optional plugins.");
        if (ImGui.Checkbox("Enable online notifications", ref onlineNotifs))
        {
            _configService.Current.ShowOnlineNotifications = onlineNotifs;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Enabling this will show a small notification (type: Info) in the bottom right corner when pairs go online.");

        using (ImRaii.Disabled(!onlineNotifs))
        {
            using var indent = ImRaii.PushIndent();
            if (ImGui.Checkbox("Notify only for individual pairs", ref onlineNotifsPairsOnly))
            {
                _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs = onlineNotifsPairsOnly;
                _configService.Save();
            }
            UiSharedService.DrawHelpText("Enabling this will only show online notifications (type: Info) for individual pairs.");
            if (ImGui.Checkbox("Notify only for named pairs", ref onlineNotifsNamedOnly))
            {
                _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs = onlineNotifsNamedOnly;
                _configService.Save();
            }
            UiSharedService.DrawHelpText("Enabling this will only show online notifications (type: Info) for pairs where you have set an individual note.");
        }
    }

    private void DrawServerConfiguration()
    {
        _lastTab = "Service Settings";
        if (ApiController.ServerAlive)
        {
            UiSharedService.FontText("Service Actions", _uiShared.UidFont);
            ImGuiHelpers.ScaledDummy(new Vector2(5, 5));
            if (ImGui.Button("Delete all my files"))
            {
                _deleteFilesPopupModalShown = true;
                ImGui.OpenPopup("Delete all your files?");
            }

            UiSharedService.DrawHelpText("Completely deletes all your uploaded files on the service.");

            if (ImGui.BeginPopupModal("Delete all your files?", ref _deleteFilesPopupModalShown, UiSharedService.PopupWindowFlags))
            {
                UiSharedService.TextWrapped(
                    "All your own uploaded files on the service will be deleted.\nThis operation cannot be undone.");
                ImGui.TextUnformatted("Are you sure you want to continue?");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                 ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("Delete everything", new Vector2(buttonSize, 0)))
                {
                    _ = Task.Run(_fileTransferManager.DeleteAllFiles);
                    _deleteFilesPopupModalShown = false;
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteFilesPopupModalShown = false;
                }

                UiSharedService.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Delete account"))
            {
                _deleteAccountPopupModalShown = true;
                ImGui.OpenPopup("Delete your account?");
            }

            UiSharedService.DrawHelpText("Completely deletes your account and all uploaded files to the service.");

            if (ImGui.BeginPopupModal("Delete your account?", ref _deleteAccountPopupModalShown, UiSharedService.PopupWindowFlags))
            {
                UiSharedService.TextWrapped(
                    "Your account and all associated files and data on the service will be deleted.");
                UiSharedService.TextWrapped("Your UID will be removed from all pairing lists.");
                ImGui.TextUnformatted("Are you sure you want to continue?");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                  ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("Delete account", new Vector2(buttonSize, 0)))
                {
                    _ = Task.Run(ApiController.UserDelete);
                    _deleteAccountPopupModalShown = false;
                    Mediator.Publish(new SwitchToIntroUiMessage());
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteAccountPopupModalShown = false;
                }

                UiSharedService.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
            ImGui.Separator();
        }

        UiSharedService.FontText("Service & Character Settings", _uiShared.UidFont);

        var idx = _uiShared.DrawServiceSelection();

        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));

        var selectedServer = _serverConfigurationManager.GetServerByIndex(idx);
        if (selectedServer == _serverConfigurationManager.CurrentServer)
        {
            UiSharedService.ColorTextWrapped("For any changes to be applied to the current service you need to reconnect to the service.", ImGuiColors.DalamudYellow);
        }

        if (ImGui.BeginTabBar("serverTabBar"))
        {
            if (ImGui.BeginTabItem("Character Management"))
            {
                if (selectedServer.SecretKeys.Any())
                {
                    UiSharedService.ColorTextWrapped("Characters listed here will automatically connect to the selected service with the settings as provided below." +
                        " Make sure to enter the character names correctly or use the 'Add current character' button at the bottom.", ImGuiColors.DalamudYellow);
                    int i = 0;
                    foreach (var item in selectedServer.Authentications.ToList())
                    {
                        using var charaId = ImRaii.PushId("selectedChara" + i);

                        var worldIdx = (ushort)item.WorldId;
                        var data = _uiShared.WorldData.OrderBy(u => u.Value, StringComparer.Ordinal).ToDictionary(k => k.Key, k => k.Value);
                        if (!data.TryGetValue(worldIdx, out string? worldPreview))
                        {
                            worldPreview = data.First().Value;
                        }

                        var secretKeyIdx = item.SecretKeyIdx;
                        var keys = selectedServer.SecretKeys;
                        if (!keys.TryGetValue(secretKeyIdx, out var secretKey))
                        {
                            secretKey = new();
                        }
                        var friendlyName = secretKey.FriendlyName;

                        if (ImGui.TreeNode($"chara", $"Character: {item.CharacterName}, World: {worldPreview}, Secret Key: {friendlyName}"))
                        {
                            var charaName = item.CharacterName;
                            if (ImGui.InputText("Character Name", ref charaName, 64))
                            {
                                item.CharacterName = charaName;
                                _serverConfigurationManager.Save();
                            }

                            _uiShared.DrawCombo("World##" + item.CharacterName + i, data, (w) => w.Value,
                                (w) =>
                                {
                                    if (item.WorldId != w.Key)
                                    {
                                        item.WorldId = w.Key;
                                        _serverConfigurationManager.Save();
                                    }
                                }, EqualityComparer<KeyValuePair<ushort, string>>.Default.Equals(data.FirstOrDefault(f => f.Key == worldIdx), default) ? data.First() : data.First(f => f.Key == worldIdx));

                            _uiShared.DrawCombo("Secret Key##" + item.CharacterName + i, keys, (w) => w.Value.FriendlyName,
                                (w) =>
                                {
                                    if (w.Key != item.SecretKeyIdx)
                                    {
                                        item.SecretKeyIdx = w.Key;
                                        _serverConfigurationManager.Save();
                                    }
                                }, EqualityComparer<KeyValuePair<int, SecretKey>>.Default.Equals(keys.FirstOrDefault(f => f.Key == item.SecretKeyIdx), default) ? keys.First() : keys.First(f => f.Key == item.SecretKeyIdx));

                            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Delete Character") && UiSharedService.CtrlPressed())
                                _serverConfigurationManager.RemoveCharacterFromServer(idx, item);
                            UiSharedService.AttachToolTip("Hold CTRL to delete this entry.");

                            ImGui.TreePop();
                        }

                        i++;
                    }

                    ImGui.Separator();
                    if (!selectedServer.Authentications.Exists(c => string.Equals(c.CharacterName, _uiShared.PlayerName, StringComparison.Ordinal)
                        && c.WorldId == _uiShared.WorldId))
                    {
                        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.User, "Add current character"))
                        {
                            _serverConfigurationManager.AddCurrentCharacterToServer(idx);
                        }
                        ImGui.SameLine();
                    }

                    if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Plus, "Add new character"))
                    {
                        _serverConfigurationManager.AddEmptyCharacterToServer(idx);
                    }
                }
                else
                {
                    UiSharedService.ColorTextWrapped("You need to add a Secret Key first before adding Characters.", ImGuiColors.DalamudYellow);
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Secret Key Management"))
            {
                foreach (var item in selectedServer.SecretKeys.ToList())
                {
                    using var id = ImRaii.PushId("key" + item.Key);
                    var friendlyName = item.Value.FriendlyName;
                    if (ImGui.InputText("Secret Key Display Name", ref friendlyName, 255))
                    {
                        item.Value.FriendlyName = friendlyName;
                        _serverConfigurationManager.Save();
                    }
                    var key = item.Value.Key;
                    var keyInUse = selectedServer.Authentications.Exists(p => p.SecretKeyIdx == item.Key);
                    if (keyInUse) ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey3);
                    if (ImGui.InputText("Secret Key", ref key, 64))
                    {
                        item.Value.Key = key;
                        _serverConfigurationManager.Save();
                    }
                    if (keyInUse) ImGui.PopStyleColor();
                    if (!keyInUse)
                    {
                        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Delete Secret Key") && UiSharedService.CtrlPressed())
                        {
                            selectedServer.SecretKeys.Remove(item.Key);
                            _serverConfigurationManager.Save();
                        }
                        UiSharedService.AttachToolTip("Hold CTRL to delete this secret key entry");
                    }
                    else
                    {
                        UiSharedService.ColorTextWrapped("This key is in use and cannot be edited or deleted", ImGuiColors.DalamudYellow);
                    }

                    if (item.Key != selectedServer.SecretKeys.Keys.LastOrDefault())
                        ImGui.Separator();
                }

                ImGui.Separator();
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Plus, "Add new Secret Key"))
                {
                    selectedServer.SecretKeys.Add(selectedServer.SecretKeys.Any() ? selectedServer.SecretKeys.Max(p => p.Key) + 1 : 0, new SecretKey()
                    {
                        FriendlyName = "New Secret Key",
                    });
                    _serverConfigurationManager.Save();
                }

                if (selectedServer.ServerUri == ApiController.LoporritServiceUri)
                {
                    ImGui.SameLine();
                    if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Plus, "Register a new Loporrit account"))
                    {
                        _registrationInProgress = true;
                        _ = Task.Run(async () => {
                            try
                            {
                                using HttpClient httpClient = new();
                                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", ver!.Major + "." + ver!.Minor + "." + ver!.Build));
                                var postUri = MareAuth.AuthRegisterFullPath(new Uri(selectedServer.ServerUri
                                    .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                                    .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
                                _logger.LogInformation("Registering new account: " + postUri.ToString());
                                var result = await httpClient.PostAsync(postUri, null).ConfigureAwait(false);
                                result.EnsureSuccessStatusCode();
                                var reply = await result.Content.ReadFromJsonAsync<RegisterReplyDto>().ConfigureAwait(false) ?? new();
                                if (!reply.Success)
                                {
                                    _logger.LogWarning("Registration failed: " + reply.ErrorMessage);
                                    _registrationMessage = reply.ErrorMessage;
                                    if (_registrationMessage.IsNullOrEmpty())
                                        _registrationMessage = "An unknown error occured. Please try again later.";
                                    return;
                                }
                                _registrationMessage = "New account registered.\nPlease keep a copy of your secret key in case you need to reset your plugins, or to use it on another PC.";
                                _registrationSuccess = true;
                                selectedServer.SecretKeys.Add(selectedServer.SecretKeys.Any() ? selectedServer.SecretKeys.Max(p => p.Key) + 1 : 0, new SecretKey()
                                {
                                    FriendlyName = reply.UID + $" (registered {DateTime.Now:yyyy-MM-dd})",
                                    Key = reply.SecretKey ?? ""
                                });
                                _serverConfigurationManager.Save();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Registration failed");
                                _registrationSuccess = false;
                                _registrationMessage = "An unknown error occured. Please try again later.";
                            }
                            finally
                            {
                                _registrationInProgress = false;
                            }
                        });
                    }
                    if (_registrationInProgress)
                    {
                        ImGui.TextUnformatted("Sending request...");
                    }
                    else if (!_registrationMessage.IsNullOrEmpty())
                    {
                        if (!_registrationSuccess)
                            ImGui.TextColored(ImGuiColors.DalamudYellow, _registrationMessage);
                        else
                            ImGui.TextUnformatted(_registrationMessage);
                    }
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Service Settings"))
            {
                var serverName = selectedServer.ServerName;
                var serverUri = selectedServer.ServerUri;
                var isMain = string.Equals(serverName, ApiController.LoporritServer, StringComparison.OrdinalIgnoreCase);
                var flags = isMain ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None;

                if (ImGui.InputText("Service URI", ref serverUri, 255, flags))
                {
                    selectedServer.ServerUri = serverUri;
                }
                if (isMain)
                {
                    UiSharedService.DrawHelpText("You cannot edit the URI of the main service.");
                }

                if (ImGui.InputText("Service Name", ref serverName, 255, flags))
                {
                    selectedServer.ServerName = serverName;
                    _serverConfigurationManager.Save();
                }
                if (isMain)
                {
                    UiSharedService.DrawHelpText("You cannot edit the name of the main service.");
                }

                if (!isMain && selectedServer != _serverConfigurationManager.CurrentServer)
                {
                    if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Delete Service") && UiSharedService.CtrlPressed())
                    {
                        _serverConfigurationManager.DeleteServer(selectedServer);
                    }
                    UiSharedService.DrawHelpText("Hold CTRL to delete this service");
                }
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawSettingsContent()
    {
        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.TextUnformatted("Service " + _serverConfigurationManager.CurrentServer!.ServerName + ":");
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.ParsedGreen, "Available");
            ImGui.SameLine();
            ImGui.TextUnformatted("(");
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.ParsedGreen, _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture));
            ImGui.SameLine();
            ImGui.TextUnformatted("Users Online");
            ImGui.SameLine();
            ImGui.TextUnformatted(")");
        }
        ImGui.Separator();
        if (ImGui.BeginTabBar("mainTabBar"))
        {
            if (ImGui.BeginTabItem("General"))
            {
                DrawGeneral();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Export & Storage"))
            {
                DrawFileStorageSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Transfers"))
            {
                DrawCurrentTransfers();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Service Settings"))
            {
                ImGui.BeginDisabled(_registrationInProgress);
                DrawServerConfiguration();
                ImGui.EndTabItem();
                ImGui.EndDisabled(); // _registrationInProgress
            }

            if (ImGui.BeginTabItem("Chat"))
            {
                DrawChatConfig();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Debug"))
            {
                DrawDebug();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void UiSharedService_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void UiSharedService_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }
}
