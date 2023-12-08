﻿using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.WebAPI;
using System.Globalization;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

internal class SyncshellAdminPopupHandler : IPopupHandler
{
    private readonly ApiController _apiController;
    private readonly List<string> _oneTimeInvites = [];
    private readonly PairManager _pairManager;
    private readonly UiSharedService _uiSharedService;
    private List<BannedGroupUserDto> _bannedUsers = [];
    private GroupFullInfoDto _groupFullInfo = null!;
    private bool _isModerator = false;
    private bool _isOwner = false;
    private int _multiInvites = 30;
    private string _newPassword = string.Empty;
    private bool _pwChangeSuccess = true;

    public SyncshellAdminPopupHandler(ApiController apiController, UiSharedService uiSharedService, PairManager pairManager)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
    }

    public Vector2 PopupSize => new(700, 500);

    public void DrawContent()
    {
        if (!_isModerator && !_isOwner) return;

        _groupFullInfo = _pairManager.Groups[_groupFullInfo.Group];

        using (ImRaii.PushFont(_uiSharedService.UidFont))
            ImGui.TextUnformatted(_groupFullInfo.GroupAliasOrGID + " Administrative Panel");

        ImGui.Separator();
        var perm = _groupFullInfo.GroupPermissions;

        var inviteNode = ImRaii.TreeNode("Invites");
        if (inviteNode)
        {
            bool isInvitesDisabled = perm.IsDisableInvites();

            if (UiSharedService.IconTextButton(isInvitesDisabled ? FontAwesomeIcon.Unlock : FontAwesomeIcon.Lock,
                isInvitesDisabled ? "Unlock Syncshell" : "Lock Syncshell"))
            {
                perm.SetDisableInvites(!isInvitesDisabled);
                _ = _apiController.GroupChangeGroupPermissionState(new(_groupFullInfo.Group, perm));
            }

            ImGui.Dummy(new(2f));

            UiSharedService.TextWrapped("One-time invites work as single-use passwords. Use those if you do not want to distribute your Syncshell password.");
            if (UiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Single one-time invite"))
            {
                ImGui.SetClipboardText(_apiController.GroupCreateTempInvite(new(_groupFullInfo.Group), 1).Result.FirstOrDefault() ?? string.Empty);
            }
            UiSharedService.AttachToolTip("Creates a single-use password for joining the syncshell which is valid for 24h and copies it to the clipboard.");
            ImGui.InputInt("##amountofinvites", ref _multiInvites);
            ImGui.SameLine();
            using (ImRaii.Disabled(_multiInvites <= 1 || _multiInvites > 100))
            {
                if (UiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Generate " + _multiInvites + " one-time invites"))
                {
                    _oneTimeInvites.AddRange(_apiController.GroupCreateTempInvite(new(_groupFullInfo.Group), _multiInvites).Result);
                }
            }

            if (_oneTimeInvites.Any())
            {
                var invites = string.Join(Environment.NewLine, _oneTimeInvites);
                ImGui.InputTextMultiline("Generated Multi Invites", ref invites, 5000, new(0, 0), ImGuiInputTextFlags.ReadOnly);
                if (UiSharedService.IconTextButton(FontAwesomeIcon.Copy, "Copy Invites to clipboard"))
                {
                    ImGui.SetClipboardText(invites);
                }
            }
        }
        inviteNode.Dispose();

        var mgmtNode = ImRaii.TreeNode("User Management");
        if (mgmtNode)
        {
            if (UiSharedService.IconTextButton(FontAwesomeIcon.Broom, "Clear Syncshell"))
            {
                _ = _apiController.GroupClear(new(_groupFullInfo.Group));
            }
            UiSharedService.AttachToolTip("This will remove all non-pinned, non-moderator users from the Syncshell");

            ImGui.Dummy(new(2f));

            if (UiSharedService.IconTextButton(FontAwesomeIcon.Retweet, "Refresh Banlist from Server"))
            {
                _bannedUsers = _apiController.GroupGetBannedUsers(new GroupDto(_groupFullInfo.Group)).Result;
            }

            if (ImGui.BeginTable("bannedusertable" + _groupFullInfo.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
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
                    if (UiSharedService.IconTextButton(FontAwesomeIcon.Check, "Unban#" + bannedUser.UID))
                    {
                        _ = _apiController.GroupUnbanUser(bannedUser);
                        _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
                    }
                }

                ImGui.EndTable();
            }
        }
        mgmtNode.Dispose();

        var permNode = ImRaii.TreeNode("Permissions");
        if (permNode)
        {
            bool isDisableAnimations = perm.IsDisableAnimations();
            bool isDisableSounds = perm.IsDisableSounds();
            bool isDisableVfx = perm.IsDisableVFX();

            ImGui.AlignTextToFramePadding();
            ImGui.Text("Sound Sync");
            UiSharedService.BooleanToColoredIcon(!isDisableSounds);
            ImGui.SameLine(230);
            if (UiSharedService.IconTextButton(isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute,
                isDisableSounds ? "Enable sound sync" : "Disable sound sync"))
            {
                perm.SetDisableSounds(!perm.IsDisableSounds());
                _ = _apiController.GroupChangeGroupPermissionState(new(_groupFullInfo.Group, perm));
            }

            ImGui.AlignTextToFramePadding();
            ImGui.Text("Animation Sync");
            UiSharedService.BooleanToColoredIcon(!isDisableAnimations);
            ImGui.SameLine(230);
            if (UiSharedService.IconTextButton(isDisableAnimations ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop,
                isDisableAnimations ? "Enable animation sync" : "Disable animation sync"))
            {
                perm.SetDisableAnimations(!perm.IsDisableAnimations());
                _ = _apiController.GroupChangeGroupPermissionState(new(_groupFullInfo.Group, perm));
            }

            ImGui.AlignTextToFramePadding();
            ImGui.Text("VFX Sync");
            UiSharedService.BooleanToColoredIcon(!isDisableVfx);
            ImGui.SameLine(230);
            if (UiSharedService.IconTextButton(isDisableVfx ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle,
                isDisableVfx ? "Enable VFX sync" : "Disable VFX sync"))
            {
                perm.SetDisableVFX(!perm.IsDisableVFX());
                _ = _apiController.GroupChangeGroupPermissionState(new(_groupFullInfo.Group, perm));
            }
        }
        permNode.Dispose();

        if (_isOwner)
        {
            var ownerNode = ImRaii.TreeNode("Owner Settings");
            if (ownerNode)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("New Password");
                ImGui.SameLine();
                ImGui.InputTextWithHint("##changepw", "Min 10 characters", ref _newPassword, 50);
                ImGui.SameLine();
                using (ImRaii.Disabled(_newPassword.Length < 10))
                {
                    if (UiSharedService.IconTextButton(FontAwesomeIcon.Passport, "Change Password"))
                    {
                        _pwChangeSuccess = _apiController.GroupChangePassword(new GroupPasswordDto(_groupFullInfo.Group, _newPassword)).Result;
                        _newPassword = string.Empty;
                    }
                }
                UiSharedService.AttachToolTip("Password requires to be at least 10 characters long. This action is irreversible.");

                if (!_pwChangeSuccess)
                {
                    UiSharedService.ColorTextWrapped("Failed to change the password. Password requires to be at least 10 characters long.", ImGuiColors.DalamudYellow);
                }

                if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Syncshell") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                {
                    ImGui.CloseCurrentPopup();
                    _ = _apiController.GroupDelete(new(_groupFullInfo.Group));
                }
                UiSharedService.AttachToolTip("Hold CTRL and Shift and click to delete this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible.");
            }
            ownerNode.Dispose();
        }
    }

    public void Open(GroupFullInfoDto groupFullInfo)
    {
        _groupFullInfo = groupFullInfo;
        _isOwner = string.Equals(_groupFullInfo.OwnerUID, _apiController.UID, StringComparison.Ordinal);
        _isModerator = _groupFullInfo.GroupUserInfo.IsModerator();
        _newPassword = string.Empty;
        _bannedUsers.Clear();
        _oneTimeInvites.Clear();
        _multiInvites = 30;
        _pwChangeSuccess = true;
    }
}