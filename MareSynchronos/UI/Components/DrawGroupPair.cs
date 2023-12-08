﻿using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.WebAPI;
using MareSynchronos.API.Dto.User;
using MareSynchronos.UI.Handlers;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.Services.Mediator;
using Dalamud.Interface.Utility;
using MareSynchronos.UI.Components.Popup;

namespace MareSynchronos.UI.Components;

public class DrawGroupPair : DrawPairBase
{
    protected readonly MareMediator _mediator;
    private readonly GroupPairFullInfoDto _fullInfoDto;
    private readonly GroupFullInfoDto _group;

    public DrawGroupPair(string id, Pair entry, ApiController apiController, MareMediator mareMediator, GroupFullInfoDto group, GroupPairFullInfoDto fullInfoDto, UidDisplayHandler handler) : base(id, entry, apiController, handler)
    {
        _group = group;
        _fullInfoDto = fullInfoDto;
        _mediator = mareMediator;
    }

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        var entryUID = _pair.UserData.AliasOrUID;
        var entryIsMod = _fullInfoDto.GroupPairStatusInfo.IsModerator();
        var entryIsOwner = string.Equals(_pair.UserData.UID, _group.OwnerUID, StringComparison.Ordinal);
        var entryIsPinned = _fullInfoDto.GroupPairStatusInfo.IsPinned();
        var presenceIcon = _pair.IsVisible ? FontAwesomeIcon.Eye : (_pair.IsOnline ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink);
        var presenceColor = (_pair.IsOnline || _pair.IsVisible) ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var presenceText = entryUID + " is offline";

        ImGui.SetCursorPosY(textPosY);
        if (_pair.IsPaused)
        {
            presenceIcon = FontAwesomeIcon.Question;
            presenceColor = ImGuiColors.DalamudGrey;
            presenceText = entryUID + " online status is unknown (paused)";

            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.PauseCircle.ToIconString(), ImGuiColors.DalamudYellow);
            ImGui.PopFont();

            UiSharedService.AttachToolTip("Pairing status with " + entryUID + " is paused");
        }
        else
        {
            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.Check.ToIconString(), ImGuiColors.ParsedGreen);
            ImGui.PopFont();

            UiSharedService.AttachToolTip("You are paired with " + entryUID);
        }

        if (_pair.IsOnline && !_pair.IsVisible) presenceText = entryUID + " is online";
        else if (_pair.IsOnline && _pair.IsVisible) presenceText = entryUID + " is visible: " + _pair.PlayerName;

        ImGui.SameLine();
        ImGui.SetCursorPosY(textPosY);
        ImGui.PushFont(UiBuilder.IconFont);
        UiSharedService.ColorText(presenceIcon.ToIconString(), presenceColor);
        ImGui.PopFont();
        UiSharedService.AttachToolTip(presenceText);

        if (entryIsOwner)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Crown.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("User is owner of this Syncshell");
        }
        else if (entryIsMod)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.UserShield.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("User is moderator of this Syncshell");
        }
        else if (entryIsPinned)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Thumbtack.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("User is pinned in this Syncshell");
        }
    }

    protected override float DrawRightSide(float textPosY, float originalY)
    {
        var entryUID = _fullInfoDto.UserAliasOrUID;
        var entryIsMod = _fullInfoDto.GroupPairStatusInfo.IsModerator();
        var entryIsOwner = string.Equals(_pair.UserData.UID, _group.OwnerUID, StringComparison.Ordinal);
        var entryIsPinned = _fullInfoDto.GroupPairStatusInfo.IsPinned();
        var userIsOwner = string.Equals(_group.OwnerUID, _apiController.UID, StringComparison.OrdinalIgnoreCase);
        var userIsModerator = _group.GroupUserInfo.IsModerator();

        var soundsDisabled = _fullInfoDto.GroupUserPermissions.IsDisableSounds();
        var animDisabled = _fullInfoDto.GroupUserPermissions.IsDisableAnimations();
        var vfxDisabled = _fullInfoDto.GroupUserPermissions.IsDisableVFX();
        var individualSoundsDisabled = (_pair.UserPair?.OwnPermissions.IsDisableSounds() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
        var individualAnimDisabled = (_pair.UserPair?.OwnPermissions.IsDisableAnimations() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
        var individualVFXDisabled = (_pair.UserPair?.OwnPermissions.IsDisableVFX() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);

        bool showInfo = (individualAnimDisabled || individualSoundsDisabled || animDisabled || soundsDisabled);
        bool showPlus = _pair.UserPair == null;
        bool showBars = (userIsOwner || (userIsModerator && !entryIsMod && !entryIsOwner)) || !_pair.IsPaused;

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var permIcon = (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled) ? FontAwesomeIcon.ExclamationTriangle
            : ((soundsDisabled || animDisabled || vfxDisabled) ? FontAwesomeIcon.InfoCircle : FontAwesomeIcon.None);
        var infoIconWidth = UiSharedService.GetIconSize(permIcon).X;
        var plusButtonWidth = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus).X;
        var barButtonWidth = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars).X;

        var pos = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() + spacing
            - (showInfo ? (infoIconWidth + spacing) : 0)
            - (showPlus ? (plusButtonWidth + spacing) : 0)
            - (showBars ? (barButtonWidth + spacing) : 0);

        ImGui.SameLine(pos);
        if (individualAnimDisabled || individualSoundsDisabled)
        {
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            UiSharedService.FontText(permIcon.ToIconString(), UiBuilder.IconFont);
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();

                ImGui.Text("Individual User permissions");

                if (individualSoundsDisabled)
                {
                    var userSoundsText = "Sound sync disabled with " + _pair.UserData.AliasOrUID;
                    UiSharedService.FontText(FontAwesomeIcon.VolumeOff.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userSoundsText);
                    ImGui.NewLine();
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text("You: " + (_pair.UserPair!.OwnPermissions.IsDisableSounds() ? "Disabled" : "Enabled") + ", They: " + (_pair.UserPair!.OtherPermissions.IsDisableSounds() ? "Disabled" : "Enabled"));
                }

                if (individualAnimDisabled)
                {
                    var userAnimText = "Animation sync disabled with " + _pair.UserData.AliasOrUID;
                    UiSharedService.FontText(FontAwesomeIcon.Stop.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userAnimText);
                    ImGui.NewLine();
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text("You: " + (_pair.UserPair!.OwnPermissions.IsDisableAnimations() ? "Disabled" : "Enabled") + ", They: " + (_pair.UserPair!.OtherPermissions.IsDisableAnimations() ? "Disabled" : "Enabled"));
                }

                if (individualVFXDisabled)
                {
                    var userVFXText = "VFX sync disabled with " + _pair.UserData.AliasOrUID;
                    UiSharedService.FontText(FontAwesomeIcon.Circle.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userVFXText);
                    ImGui.NewLine();
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text("You: " + (_pair.UserPair!.OwnPermissions.IsDisableVFX() ? "Disabled" : "Enabled") + ", They: " + (_pair.UserPair!.OtherPermissions.IsDisableVFX() ? "Disabled" : "Enabled"));
                }

                ImGui.EndTooltip();
            }
            ImGui.SameLine();
        }
        else if ((animDisabled || soundsDisabled))
        {
            ImGui.SetCursorPosY(textPosY);
            UiSharedService.FontText(permIcon.ToIconString(), UiBuilder.IconFont);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();

                ImGui.Text("Sycnshell User permissions");

                if (soundsDisabled)
                {
                    var userSoundsText = "Sound sync disabled by " + _pair.UserData.AliasOrUID;
                    UiSharedService.FontText(FontAwesomeIcon.VolumeOff.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userSoundsText);
                }

                if (animDisabled)
                {
                    var userAnimText = "Animation sync disabled by " + _pair.UserData.AliasOrUID;
                    UiSharedService.FontText(FontAwesomeIcon.Stop.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userAnimText);
                }

                if (vfxDisabled)
                {
                    var userVFXText = "VFX sync disabled by " + _pair.UserData.AliasOrUID;
                    UiSharedService.FontText(FontAwesomeIcon.Circle.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userVFXText);
                }

                ImGui.EndTooltip();
            }
            ImGui.SameLine();
        }

        if (showPlus)
        {
            ImGui.SetCursorPosY(originalY);

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                _ = _apiController.UserAddPair(new UserDto(new(_pair.UserData.UID)));
            }
            UiSharedService.AttachToolTip("Pair with " + entryUID + " individually");
            ImGui.SameLine();
        }

        if (showBars)
        {
            ImGui.SetCursorPosY(originalY);

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
            {
                ImGui.OpenPopup("Popup");
            }
        }

        if (ImGui.BeginPopup("Popup"))
        {
            if ((userIsModerator || userIsOwner) && !(entryIsMod || entryIsOwner))
            {
                var pinText = entryIsPinned ? "Unpin user" : "Pin user";
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Thumbtack, pinText))
                {
                    ImGui.CloseCurrentPopup();
                    var userInfo = _fullInfoDto.GroupPairStatusInfo ^ GroupUserInfo.IsPinned;
                    _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(_fullInfoDto.Group, _fullInfoDto.User, userInfo));
                }
                UiSharedService.AttachToolTip("Pin this user to the Syncshell. Pinned users will not be deleted in case of a manually initiated Syncshell clean");

                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Remove user") && UiSharedService.CtrlPressed())
                {
                    ImGui.CloseCurrentPopup();
                    _ = _apiController.GroupRemoveUser(_fullInfoDto);
                }

                UiSharedService.AttachToolTip("Hold CTRL and click to remove user " + (_pair.UserData.AliasOrUID) + " from Syncshell");
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.UserSlash, "Ban User"))
                {
                    ImGui.CloseCurrentPopup();
                    _mediator.Publish(new OpenBanUserPopupMessage(_pair, _group));
                }
                UiSharedService.AttachToolTip("Ban user from this Syncshell");
            }

            if (userIsOwner)
            {
                string modText = entryIsMod ? "Demod user" : "Mod user";
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.UserShield, modText) && UiSharedService.CtrlPressed())
                {
                    ImGui.CloseCurrentPopup();
                    var userInfo = _fullInfoDto.GroupPairStatusInfo ^ GroupUserInfo.IsModerator;
                    _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(_fullInfoDto.Group, _fullInfoDto.User, userInfo));
                }
                UiSharedService.AttachToolTip("Hold CTRL to change the moderator status for " + (_fullInfoDto.UserAliasOrUID) + Environment.NewLine +
                    "Moderators can kick, ban/unban, pin/unpin users and clear the Syncshell.");
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Crown, "Transfer Ownership") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                {
                    ImGui.CloseCurrentPopup();
                    _ = _apiController.GroupChangeOwnership(_fullInfoDto);
                }
                UiSharedService.AttachToolTip("Hold CTRL and SHIFT and click to transfer ownership of this Syncshell to " + (_fullInfoDto.UserAliasOrUID) + Environment.NewLine + "WARNING: This action is irreversible.");
            }

            ImGui.Separator();
            if (!_pair.IsPaused)
            {
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.User, "Open Profile"))
                {
                    _displayHandler.OpenProfile(_pair);
                    ImGui.CloseCurrentPopup();
                }
                UiSharedService.AttachToolTip("Opens the profile for this user in a new window");
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.ExclamationTriangle, "Report Mare Profile"))
                {
                    ImGui.CloseCurrentPopup();
                    _mediator.Publish(new OpenReportPopupMessage(_pair));
                }
                UiSharedService.AttachToolTip("Report this users Mare Profile to the administrative team");
            }
            ImGui.EndPopup();
        }

        return pos - spacing;
    }
}