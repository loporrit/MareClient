﻿using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace MareSynchronos.UI.Components.Popup;

public class SyncshellAdminUI : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly bool _isModerator = false;
    private readonly bool _isOwner = false;
    private readonly List<string> _oneTimeInvites = [];
    private readonly PairManager _pairManager;
    private readonly UiSharedService _uiSharedService;
    private List<BannedGroupUserDto> _bannedUsers = [];
    private int _multiInvites;
    private string _newPassword;
    private bool _pwChangeSuccess;
    public SyncshellAdminUI(ILogger<SyncshellAdminUI> logger, MareMediator mediator, ApiController apiController,
        UiSharedService uiSharedService, PairManager pairManager, GroupFullInfoDto groupFullInfo)
        : base(logger, mediator, "Syncshell Admin Panel (" + groupFullInfo.GID + ")")
    {
        GroupFullInfo = groupFullInfo;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _isOwner = string.Equals(GroupFullInfo.OwnerUID, _apiController.UID, StringComparison.Ordinal);
        _isModerator = GroupFullInfo.GroupUserInfo.IsModerator();
        _newPassword = string.Empty;
        _multiInvites = 30;
        _pwChangeSuccess = true;
        IsOpen = true;
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new(700, 500),
            MaximumSize = new(700, 2000),
        };
    }

    public GroupFullInfoDto GroupFullInfo { get; private set; }

    public override void Draw()
    {
        if (!_isModerator && !_isOwner) return;

        GroupFullInfo = _pairManager.Groups[GroupFullInfo.Group];

        using var id = ImRaii.PushId("syncshell_admin_" + GroupFullInfo.GID);

        using (ImRaii.PushFont(_uiSharedService.UidFont))
            ImGui.TextUnformatted(GroupFullInfo.GroupAliasOrGID + " Administrative Panel");

        ImGui.Separator();
        var perm = GroupFullInfo.GroupPermissions;

        using var tabbar = ImRaii.TabBar("syncshell_tab_" + GroupFullInfo.GID);

        if (tabbar)
        {
            var inviteTab = ImRaii.TabItem("Invites");
            if (inviteTab)
            {
                bool isInvitesDisabled = perm.IsDisableInvites();

                if (UiSharedService.NormalizedIconTextButton(isInvitesDisabled ? FontAwesomeIcon.Unlock : FontAwesomeIcon.Lock,
                    isInvitesDisabled ? "Unlock Syncshell" : "Lock Syncshell"))
                {
                    perm.SetDisableInvites(!isInvitesDisabled);
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGuiHelpers.ScaledDummy(2f);

                UiSharedService.TextWrapped("One-time invites work as single-use passwords. Use those if you do not want to distribute your Syncshell password.");
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Envelope, "Single one-time invite"))
                {
                    ImGui.SetClipboardText(_apiController.GroupCreateTempInvite(new(GroupFullInfo.Group), 1).Result.FirstOrDefault() ?? string.Empty);
                }
                UiSharedService.AttachToolTip("Creates a single-use password for joining the syncshell which is valid for 24h and copies it to the clipboard.");
                ImGui.InputInt("##amountofinvites", ref _multiInvites);
                ImGui.SameLine();
                using (ImRaii.Disabled(_multiInvites <= 1 || _multiInvites > 100))
                {
                    if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Envelope, "Generate " + _multiInvites + " one-time invites"))
                    {
                        _oneTimeInvites.AddRange(_apiController.GroupCreateTempInvite(new(GroupFullInfo.Group), _multiInvites).Result);
                    }
                }

                if (_oneTimeInvites.Any())
                {
                    var invites = string.Join(Environment.NewLine, _oneTimeInvites);
                    ImGui.InputTextMultiline("Generated Multi Invites", ref invites, 5000, new(0, 0), ImGuiInputTextFlags.ReadOnly);
                    if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Copy, "Copy Invites to clipboard"))
                    {
                        ImGui.SetClipboardText(invites);
                    }
                }
            }
            inviteTab.Dispose();

            var mgmtTab = ImRaii.TabItem("User Management");
            if (mgmtTab)
            {
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Broom, "Clear Syncshell"))
                {
                    _ = _apiController.GroupClear(new(GroupFullInfo.Group));
                }
                UiSharedService.AttachToolTip("This will remove all non-pinned, non-moderator users from the Syncshell");

                ImGuiHelpers.ScaledDummy(2f);

                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Retweet, "Refresh Banlist from Server"))
                {
                    _bannedUsers = _apiController.GroupGetBannedUsers(new GroupDto(GroupFullInfo.Group)).Result;
                }

                if (ImGui.BeginTable("bannedusertable" + GroupFullInfo.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.None, 1);
                    ImGui.TableSetupColumn("Alias", ImGuiTableColumnFlags.None, 1);
                    ImGui.TableSetupColumn("By", ImGuiTableColumnFlags.None, 1);
                    ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.None, 2);
                    ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 3);
                    ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 1);

                    ImGui.TableHeadersRow();

                    foreach (var bannedUser in _bannedUsers.ToList())
                    {
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(bannedUser.UID);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(bannedUser.UserAlias ?? string.Empty);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(bannedUser.BannedBy);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(bannedUser.BannedOn.ToLocalTime().ToString(CultureInfo.CurrentCulture));
                        ImGui.TableNextColumn();
                        UiSharedService.TextWrapped(bannedUser.Reason);
                        ImGui.TableNextColumn();
                        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Check, "Unban##" + bannedUser.UID))
                        {
                            _ = _apiController.GroupUnbanUser(bannedUser);
                            _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
                        }
                    }

                    ImGui.EndTable();
                }
            }
            mgmtTab.Dispose();

            var permissionTab = ImRaii.TabItem("Permissions");
            if (permissionTab)
            {
                bool isDisableAnimations = perm.IsDisableAnimations();
                bool isDisableSounds = perm.IsDisableSounds();
                bool isDisableVfx = perm.IsDisableVFX();

                ImGui.AlignTextToFramePadding();
                ImGui.Text("Sound Sync");
                UiSharedService.BooleanToColoredIcon(!isDisableSounds);
                ImGui.SameLine(230);
                if (UiSharedService.NormalizedIconTextButton(isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute,
                    isDisableSounds ? "Enable sound sync" : "Disable sound sync"))
                {
                    perm.SetDisableSounds(!perm.IsDisableSounds());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGui.AlignTextToFramePadding();
                ImGui.Text("Animation Sync");
                UiSharedService.BooleanToColoredIcon(!isDisableAnimations);
                ImGui.SameLine(230);
                if (UiSharedService.NormalizedIconTextButton(isDisableAnimations ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop,
                    isDisableAnimations ? "Enable animation sync" : "Disable animation sync"))
                {
                    perm.SetDisableAnimations(!perm.IsDisableAnimations());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGui.AlignTextToFramePadding();
                ImGui.Text("VFX Sync");
                UiSharedService.BooleanToColoredIcon(!isDisableVfx);
                ImGui.SameLine(230);
                if (UiSharedService.NormalizedIconTextButton(isDisableVfx ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle,
                    isDisableVfx ? "Enable VFX sync" : "Disable VFX sync"))
                {
                    perm.SetDisableVFX(!perm.IsDisableVFX());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }
            }
            permissionTab.Dispose();

            if (_isOwner)
            {
                var ownerTab = ImRaii.TabItem("Owner Settings");
                if (ownerTab)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("New Password");
                    var availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                    var buttonSize = UiSharedService.GetNormalizedIconTextButtonSize(FontAwesomeIcon.Passport, "Change Password").X;
                    var textSize = ImGui.CalcTextSize("New Password").X;
                    var spacing = ImGui.GetStyle().ItemSpacing.X;

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(availableWidth - buttonSize - textSize - spacing * 2);
                    ImGui.InputTextWithHint("##changepw", "Min 10 characters", ref _newPassword, 50);
                    ImGui.SameLine();
                    using (ImRaii.Disabled(_newPassword.Length < 10))
                    {
                        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Passport, "Change Password"))
                        {
                            _pwChangeSuccess = _apiController.GroupChangePassword(new GroupPasswordDto(GroupFullInfo.Group, _newPassword)).Result;
                            _newPassword = string.Empty;
                        }
                    }
                    UiSharedService.AttachToolTip("Password requires to be at least 10 characters long. This action is irreversible.");

                    if (!_pwChangeSuccess)
                    {
                        UiSharedService.ColorTextWrapped("Failed to change the password. Password requires to be at least 10 characters long.", ImGuiColors.DalamudYellow);
                    }

                    if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Delete Syncshell") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                    {
                        IsOpen = false;
                        _ = _apiController.GroupDelete(new(GroupFullInfo.Group));
                    }
                    UiSharedService.AttachToolTip("Hold CTRL and Shift and click to delete this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible.");
                }
                ownerTab.Dispose();
            }
        }
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}