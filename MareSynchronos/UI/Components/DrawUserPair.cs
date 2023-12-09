﻿using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using System.Numerics;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.WebAPI;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using Dalamud.Interface.Utility;

namespace MareSynchronos.UI.Components;

public class DrawUserPair : DrawPairBase
{
    protected readonly MareMediator _mediator;
    private readonly SelectGroupForPairUi _selectGroupForPairUi;

    public DrawUserPair(string id, Pair entry, UidDisplayHandler displayHandler, ApiController apiController, MareMediator mareMediator, SelectGroupForPairUi selectGroupForPairUi) : base(id, entry, apiController, displayHandler)
    {
        if (_pair.UserPair == null) throw new ArgumentException("Pair must be UserPair", nameof(entry));
        _pair = entry;
        _selectGroupForPairUi = selectGroupForPairUi;
        _mediator = mareMediator;
    }

    public bool IsOnline => _pair.IsOnline;
    public bool IsVisible => _pair.IsVisible;
    public UserPairDto UserPair => _pair.UserPair!;

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        FontAwesomeIcon connectionIcon;
        Vector4 connectionColor;
        string connectionText;
        if (!(_pair.UserPair!.OwnPermissions.IsPaired() && _pair.UserPair!.OtherPermissions.IsPaired()))
        {
            connectionIcon = FontAwesomeIcon.ArrowUp;
            connectionText = _pair.UserData.AliasOrUID + " has not added you back";
            connectionColor = ImGuiColors.DalamudRed;
        }
        else if (_pair.UserPair!.OwnPermissions.IsPaused() || _pair.UserPair!.OtherPermissions.IsPaused())
        {
            connectionIcon = FontAwesomeIcon.PauseCircle;
            connectionText = "Pairing status with " + _pair.UserData.AliasOrUID + " is paused";
            connectionColor = ImGuiColors.DalamudYellow;
        }
        else
        {
            connectionIcon = FontAwesomeIcon.Check;
            connectionText = "You are paired with " + _pair.UserData.AliasOrUID;
            connectionColor = ImGuiColors.ParsedGreen;
        }

        ImGui.SetCursorPosY(textPosY);
        ImGui.PushFont(UiBuilder.IconFont);
        UiSharedService.ColorText(connectionIcon.ToIconString(), connectionColor);
        ImGui.PopFont();
        UiSharedService.AttachToolTip(connectionText);
        if (_pair is { IsOnline: true, IsVisible: true })
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.Eye.ToIconString(), ImGuiColors.ParsedGreen);
            if (ImGui.IsItemClicked())
            {
                _mediator.Publish(new TargetPairMessage(_pair));
            }
            ImGui.PopFont();
            var visibleTooltip = _pair.UserData.AliasOrUID + " is visible: " + _pair.PlayerName! + Environment.NewLine + "Click to target this player";
            if (_pair.LastAppliedDataSize >= 0)
            {
                visibleTooltip += UiSharedService.TooltipSeparator +
                    "Loaded Mods Size: " + UiSharedService.ByteToString(_pair.LastAppliedDataSize, true);
            }

            UiSharedService.AttachToolTip(visibleTooltip);
        }
    }

    protected override float DrawRightSide(float textPosY, float originalY)
    {
        var pauseIcon = _pair.UserPair!.OwnPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseIconSize = UiSharedService.GetIconButtonSize(pauseIcon);
        var barButtonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
        var entryUID = _pair.UserData.AliasOrUID;
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
        var rightSideStart = 0f;

        if (_pair.UserPair!.OwnPermissions.IsPaired() && _pair.UserPair!.OtherPermissions.IsPaired())
        {
            var individualSoundsDisabled = (_pair.UserPair?.OwnPermissions.IsDisableSounds() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
            var individualAnimDisabled = (_pair.UserPair?.OwnPermissions.IsDisableAnimations() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
            var individualVFXDisabled = (_pair.UserPair?.OwnPermissions.IsDisableVFX() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);

            if (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled)
            {
                var infoIconPosDist = windowEndX - barButtonSize.X - spacingX - pauseIconSize.X - spacingX;
                var icon = FontAwesomeIcon.ExclamationTriangle;
                var iconwidth = UiSharedService.GetIconButtonSize(icon);

                rightSideStart = infoIconPosDist - iconwidth.X;
                ImGui.SameLine(infoIconPosDist - iconwidth.X);

                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                UiSharedService.FontText(icon.ToIconString(), UiBuilder.IconFont);
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
            }

            if (rightSideStart == 0f)
            {
                rightSideStart = windowEndX - barButtonSize.X - spacingX * 2 - pauseIconSize.X;
            }
            ImGui.SameLine(windowEndX - barButtonSize.X - spacingX - pauseIconSize.X);
            ImGui.SetCursorPosY(originalY);
            if (ImGuiComponents.IconButton(pauseIcon))
            {
                var perm = _pair.UserPair!.OwnPermissions;
                perm.SetPaused(!perm.IsPaused());
                _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
            }
            UiSharedService.AttachToolTip(!_pair.UserPair!.OwnPermissions.IsPaused()
                ? "Pause pairing with " + entryUID
                : "Resume pairing with " + entryUID);
        }

        // Flyout Menu
        if (rightSideStart == 0f)
        {
            rightSideStart = windowEndX - barButtonSize.X;
        }
        ImGui.SameLine(windowEndX - barButtonSize.X);
        ImGui.SetCursorPosY(originalY);

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("User Flyout Menu");
        }
        if (ImGui.BeginPopup("User Flyout Menu"))
        {
            using (ImRaii.PushId($"buttons-{_pair.UserData.UID}")) DrawPairedClientMenu(_pair);
            ImGui.EndPopup();
        }

        return rightSideStart;
    }

    private void DrawPairedClientMenu(Pair entry)
    {
        if (!entry.IsPaused)
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.User, "Open Profile"))
            {
                _displayHandler.OpenProfile(entry);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("Opens the profile for this user in a new window");
        }
        if (entry.IsVisible)
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Sync, "Reload last data"))
            {
                entry.ApplyLastReceivedData(forced: true);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("This reapplies the last received character data to this character");
        }

        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.PlayCircle, "Cycle pause state"))
        {
            _ = _apiController.CyclePause(entry.UserData);
            ImGui.CloseCurrentPopup();
        }
        var entryUID = entry.UserData.AliasOrUID;
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Folder, "Pair Groups"))
        {
            _selectGroupForPairUi.Open(entry);
        }
        UiSharedService.AttachToolTip("Choose pair groups for " + entryUID);

        var isDisableSounds = entry.UserPair!.OwnPermissions.IsDisableSounds();
        string disableSoundsText = isDisableSounds ? "Enable sound sync" : "Disable sound sync";
        var disableSoundsIcon = isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute;
        if (UiSharedService.NormalizedIconTextButton(disableSoundsIcon, disableSoundsText))
        {
            var permissions = entry.UserPair.OwnPermissions;
            permissions.SetDisableSounds(!isDisableSounds);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, permissions));
        }

        var isDisableAnims = entry.UserPair!.OwnPermissions.IsDisableAnimations();
        string disableAnimsText = isDisableAnims ? "Enable animation sync" : "Disable animation sync";
        var disableAnimsIcon = isDisableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop;
        if (UiSharedService.NormalizedIconTextButton(disableAnimsIcon, disableAnimsText))
        {
            var permissions = entry.UserPair.OwnPermissions;
            permissions.SetDisableAnimations(!isDisableAnims);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, permissions));
        }

        var isDisableVFX = entry.UserPair!.OwnPermissions.IsDisableVFX();
        string disableVFXText = isDisableVFX ? "Enable VFX sync" : "Disable VFX sync";
        var disableVFXIcon = isDisableVFX ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle;
        if (UiSharedService.NormalizedIconTextButton(disableVFXIcon, disableVFXText))
        {
            var permissions = entry.UserPair.OwnPermissions;
            permissions.SetDisableVFX(!isDisableVFX);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, permissions));
        }

        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Unpair Permanently") && UiSharedService.CtrlPressed())
        {
            _ = _apiController.UserRemovePair(new(entry.UserData));
        }
        UiSharedService.AttachToolTip("Hold CTRL and click to unpair permanently from " + entryUID);

        ImGui.Separator();
        if (!entry.IsPaused)
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.ExclamationTriangle, "Report Mare Profile"))
            {
                ImGui.CloseCurrentPopup();
                _mediator.Publish(new OpenReportPopupMessage(_pair));
            }
            UiSharedService.AttachToolTip("Report this users Mare Profile to the administrative team");
        }
    }
}