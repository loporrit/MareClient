﻿using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using ImGuiNET;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class EditProfileUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly FileDialogManager _fileDialogManager;
    private readonly MareProfileManager _mareProfileManager;
    private readonly UiBuilder _uiBuilder;
    private readonly UiSharedService _uiSharedService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private bool _adjustedForScollBarsLocalProfile = false;
    private bool _adjustedForScollBarsOnlineProfile = false;
    private string _descriptionText = string.Empty;
    private IDalamudTextureWrap? _pfpTextureWrap;
    private string _profileDescription = string.Empty;
    private byte[] _profileImage = Array.Empty<byte>();
    private bool _showFileDialogError = false;
    private bool _wasOpen;

    public EditProfileUi(ILogger<EditProfileUi> logger, MareMediator mediator,
        ApiController apiController, UiBuilder uiBuilder, UiSharedService uiSharedService,
        FileDialogManager fileDialogManager, ServerConfigurationManager serverConfigurationManager,
        MareProfileManager mareProfileManager) : base(logger, mediator, $"Loporrit Edit Profile###{LoporritSync.Plugin.AssemblyName}EditProfileUI")
    {
        IsOpen = false;
        this.SizeConstraints = new()
        {
            MinimumSize = new(768, 512),
            MaximumSize = new(768, 2000)
        };
        _apiController = apiController;
        _uiBuilder = uiBuilder;
        _uiSharedService = uiSharedService;
        _fileDialogManager = fileDialogManager;
        _serverConfigurationManager = serverConfigurationManager;
        _mareProfileManager = mareProfileManager;

        Mediator.Subscribe<GposeStartMessage>(this, (_) => { _wasOpen = IsOpen; IsOpen = false; });
        Mediator.Subscribe<GposeEndMessage>(this, (_) => IsOpen = _wasOpen);
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData == null || string.Equals(msg.UserData.UID, _apiController.UID, StringComparison.Ordinal))
            {
                _pfpTextureWrap?.Dispose();
                _pfpTextureWrap = null;
            }
        });
    }

    public override void Draw()
    {
        _uiSharedService.BigText("Current Profile (as saved on server)");

        var profile = _mareProfileManager.GetMareProfile(new UserData(_apiController.UID));

        if (profile.IsFlagged)
        {
            UiSharedService.ColorTextWrapped(profile.Description, ImGuiColors.DalamudRed);
            return;
        }

        if (!_profileImage.SequenceEqual(profile.ImageData.Value))
        {
            _profileImage = profile.ImageData.Value;
            _pfpTextureWrap?.Dispose();
            _pfpTextureWrap = _uiBuilder.LoadImage(_profileImage);
        }

        if (!string.Equals(_profileDescription, profile.Description, StringComparison.OrdinalIgnoreCase))
        {
            _profileDescription = profile.Description;
            _descriptionText = _profileDescription;
        }

        if (_pfpTextureWrap != null)
        {
            ImGui.Image(_pfpTextureWrap.ImGuiHandle, ImGuiHelpers.ScaledVector2(_pfpTextureWrap.Width, _pfpTextureWrap.Height));
        }

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGuiHelpers.ScaledRelativeSameLine(256, spacing);
        ImGui.PushFont(_uiBuilder.GetGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12)).ImFont);
        var descriptionTextSize = ImGui.CalcTextSize(profile.Description, 256f);
        var childFrame = ImGuiHelpers.ScaledVector2(256 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 256);
        if (descriptionTextSize.Y > childFrame.Y)
        {
            _adjustedForScollBarsOnlineProfile = true;
        }
        else
        {
            _adjustedForScollBarsOnlineProfile = false;
        }
        childFrame = childFrame with
        {
            X = childFrame.X + (_adjustedForScollBarsOnlineProfile ? ImGui.GetStyle().ScrollbarSize : 0),
        };
        if (ImGui.BeginChildFrame(101, childFrame))
        {
            UiSharedService.TextWrapped(profile.Description);
        }
        ImGui.EndChildFrame();
        ImGui.PopFont();

        var nsfw = profile.IsNSFW;
        ImGui.BeginDisabled();
        ImGui.Checkbox("Is NSFW", ref nsfw);
        ImGui.EndDisabled();

        if (_serverConfigurationManager.CurrentApiUrl == ApiController.MainServiceUri)
        {
            ImGui.Separator();
            _uiSharedService.BigText("Notes and Rules for Profiles");

            ImGui.TextWrapped($"- All users that are paired and unpaused with you will be able to see your profile picture and description.{Environment.NewLine}" +
                $"- Other users have the possibility to report your profile for breaking the rules.{Environment.NewLine}" +
                $"- !!! AVOID: anything as profile image that can be considered highly illegal or obscene (bestiality, anything that could be considered a sexual act with a minor (that includes Lalafells), etc.){Environment.NewLine}" +
                $"- !!! AVOID: slurs of any kind in the description that can be considered highly offensive{Environment.NewLine}" +
                $"- In case of valid reports from other users this can lead to disabling your profile forever or terminating your Mare account indefinitely.{Environment.NewLine}" +
                $"- Judgement of your profile validity from reports through staff is not up to debate and the decisions to disable your profile/account permanent.{Environment.NewLine}" +
                $"- If your profile picture or profile description could be considered NSFW, enable the toggle below.");
        }

        ImGui.Separator();
        _uiSharedService.BigText("Profile Settings");

        if (UiSharedService.IconTextButton(FontAwesomeIcon.FileUpload, "Upload new profile picture"))
        {
            _fileDialogManager.OpenFileDialog("Select new Profile picture", ".png", (success, file) =>
            {
                if (!success) return;
                Task.Run(async () =>
                {
                    var fileContent = File.ReadAllBytes(file);
                    using MemoryStream ms = new(fileContent);
                    var format = await Image.DetectFormatAsync(ms).ConfigureAwait(false);
                    if (!format.FileExtensions.Contains("png", StringComparer.OrdinalIgnoreCase))
                    {
                        _showFileDialogError = true;
                        return;
                    }
                    using var image = Image.Load<Rgba32>(fileContent);

                    if (image.Width > 256 || image.Height > 256 || (fileContent.Length > 250 * 1024))
                    {
                        _showFileDialogError = true;
                        return;
                    }

                    _showFileDialogError = false;
                    await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, Convert.ToBase64String(fileContent), null))
                        .ConfigureAwait(false);
                });
            });
        }
        UiSharedService.AttachToolTip("Select and upload a new profile picture");
        ImGui.SameLine();
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear uploaded profile picture"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, "", null));
        }
        UiSharedService.AttachToolTip("Clear your currently uploaded profile picture");
        if (_showFileDialogError)
        {
            UiSharedService.ColorTextWrapped("The profile picture must be a PNG file with a maximum height and width of 256px and 250KiB size", ImGuiColors.DalamudRed);
        }
        var isNsfw = profile.IsNSFW;
        if (ImGui.Checkbox("Profile is NSFW", ref isNsfw))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, isNsfw, null, null));
        }
        UiSharedService.DrawHelpText("If your profile description or image can be considered NSFW, toggle this to ON");
        var widthTextBox = 400;
        var posX = ImGui.GetCursorPosX();
        ImGui.TextUnformatted($"Description {_descriptionText.Length}/1500");
        ImGui.SetCursorPosX(posX);
        ImGuiHelpers.ScaledRelativeSameLine(widthTextBox, ImGui.GetStyle().ItemSpacing.X);
        ImGui.TextUnformatted("Preview (approximate)");
        ImGui.PushFont(_uiBuilder.GetGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12)).ImFont);
        ImGui.InputTextMultiline("##description", ref _descriptionText, 1500, ImGuiHelpers.ScaledVector2(widthTextBox, 200));
        ImGui.PopFont();

        ImGui.SameLine();

        ImGui.PushFont(_uiBuilder.GetGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12)).ImFont);
        var descriptionTextSizeLocal = ImGui.CalcTextSize(_descriptionText, 256f);
        var childFrameLocal = ImGuiHelpers.ScaledVector2(256 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 200);
        if (descriptionTextSizeLocal.Y > childFrameLocal.Y)
        {
            _adjustedForScollBarsLocalProfile = true;
        }
        else
        {
            _adjustedForScollBarsLocalProfile = false;
        }
        childFrameLocal = childFrameLocal with
        {
            X = childFrameLocal.X + (_adjustedForScollBarsLocalProfile ? ImGui.GetStyle().ScrollbarSize : 0),
        };
        if (ImGui.BeginChildFrame(102, childFrameLocal))
        {
            UiSharedService.TextWrapped(_descriptionText);
        }
        ImGui.EndChildFrame();
        ImGui.PopFont();

        if (UiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save Description"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, null, _descriptionText));
        }
        UiSharedService.AttachToolTip("Sets your profile description text");
        ImGui.SameLine();
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear Description"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, null, ""));
        }
        UiSharedService.AttachToolTip("Clears your profile description text");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _pfpTextureWrap?.Dispose();
    }
}