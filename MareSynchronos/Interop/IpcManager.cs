using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Glamourer.Api.Helpers;
using Glamourer.Api.IpcSubscribers;
using Dalamud.Utility;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;
using System.Collections.Concurrent;
using System.Text;

namespace MareSynchronos.Interop;

public sealed class IpcManager : DisposableMediatorSubscriberBase
{
    private readonly ICallGateSubscriber<(int, int)> _customizePlusApiVersion;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> _customizePlusGetActiveProfile;
    private readonly ICallGateSubscriber<Guid, (int, string?)> _customizePlusGetProfileById;
    private readonly ICallGateSubscriber<ushort, Guid, object> _customizePlusOnScaleUpdate;
    private readonly ICallGateSubscriber<ushort, int> _customizePlusRevertCharacter;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> _customizePlusSetBodyScaleToCharacter;
    private readonly ICallGateSubscriber<Guid, int> _customizePlusDeleteByUniqueId;
    private readonly IDalamudPluginInterface _pi;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly Glamourer.Api.IpcSubscribers.ApiVersion _glamourerApiVersions;
    private readonly Glamourer.Api.IpcSubscribers.ApplyState? _glamourerApplyAll;
    private readonly Glamourer.Api.IpcSubscribers.GetStateBase64? _glamourerGetAllCustomization;
    private readonly Glamourer.Api.IpcSubscribers.RevertState _glamourerRevert;
    private readonly Glamourer.Api.IpcSubscribers.RevertStateName _glamourerRevertByName;
    private readonly Glamourer.Api.IpcSubscribers.UnlockState _glamourerUnlock;
    private readonly Glamourer.Api.IpcSubscribers.UnlockStateName _glamourerUnlockByName;
    private readonly Glamourer.Api.Helpers.EventSubscriber<nint> _glamourerStateChanged;
    private readonly ICallGateSubscriber<(int, int)> _heelsGetApiVersion;
    private readonly ICallGateSubscriber<string> _heelsGetOffset;
    private readonly ICallGateSubscriber<string, object?> _heelsOffsetUpdate;
    private readonly ICallGateSubscriber<int, string, object?> _heelsRegisterPlayer;
    private readonly ICallGateSubscriber<int, object?> _heelsUnregisterPlayer;
    private readonly ICallGateSubscriber<(uint major, uint minor)> _honorificApiVersion;
    private readonly ICallGateSubscriber<int, object> _honorificClearCharacterTitle;
    private readonly ICallGateSubscriber<object> _honorificDisposing;
    private readonly ICallGateSubscriber<string> _honorificGetLocalCharacterTitle;
    private readonly ICallGateSubscriber<string, object> _honorificLocalCharacterTitleChanged;
    private readonly ICallGateSubscriber<object> _honorificReady;
    private readonly ICallGateSubscriber<int, string, object> _honorificSetCharacterTitle;
    private readonly ConcurrentDictionary<IntPtr, bool> _penumbraRedrawRequests = new();
    private readonly Penumbra.Api.Helpers.EventSubscriber _penumbraDispose;
    private readonly Penumbra.Api.Helpers.EventSubscriber<nint, string, string> _penumbraGameObjectResourcePathResolved;
    private readonly Penumbra.Api.Helpers.EventSubscriber _penumbraInit;
    private readonly Penumbra.Api.Helpers.EventSubscriber<ModSettingChange, Guid, string, bool> _penumbraModSettingChanged;
    private readonly Penumbra.Api.Helpers.EventSubscriber<nint, int> _penumbraObjectIsRedrawn;
    private readonly Penumbra.Api.IpcSubscribers.ApiVersion _penumbraApiVersion;
    private readonly Penumbra.Api.IpcSubscribers.AddTemporaryMod _penumbraAddTemporaryMod;
    private readonly Penumbra.Api.IpcSubscribers.AssignTemporaryCollection _penumbraAssignTemporaryCollection;
    private readonly Penumbra.Api.IpcSubscribers.ConvertTextureFile _penumbraConvertTextureFile;
    private readonly Penumbra.Api.IpcSubscribers.CreateTemporaryCollection _penumbraCreateNamedTemporaryCollection;
    private readonly Penumbra.Api.IpcSubscribers.GetEnabledState _penumbraEnabled;
    private readonly Penumbra.Api.IpcSubscribers.GetPlayerMetaManipulations _penumbraGetMetaManipulations;
    private readonly Penumbra.Api.IpcSubscribers.RedrawObject _penumbraRedraw;
    private readonly Penumbra.Api.IpcSubscribers.DeleteTemporaryCollection _penumbraRemoveTemporaryCollection;
    private readonly Penumbra.Api.IpcSubscribers.RemoveTemporaryMod _penumbraRemoveTemporaryMod;
    private readonly Penumbra.Api.IpcSubscribers.GetModDirectory _penumbraResolveModDir;
    private readonly Penumbra.Api.IpcSubscribers.ResolvePlayerPathsAsync _penumbraResolvePaths;
    private readonly Penumbra.Api.IpcSubscribers.GetGameObjectResourcePaths _penumbraResourcePaths;
    private readonly SemaphoreSlim _redrawSemaphore = new(2);
    private readonly uint LockCode = 0x626E7579;
    private bool _customizePlusAvailable = false;
    private CancellationTokenSource _disposalCts = new();
    private bool _glamourerAvailable = false;
    private bool _heelsAvailable = false;
    private bool _honorificAvailable = false;
    private bool _penumbraAvailable = false;
    private bool _shownGlamourerUnavailable = false;
    private bool _shownPenumbraUnavailable = false;

    private bool _useLegacyGlamourer = false;

    private readonly Glamourer.Api.IpcSubscribers.Legacy.ApiVersions _glamourerApiVersionLegacy;
    private readonly ICallGateSubscriber<string, IGameObject?, uint, object>? _glamourerApplyAllLegacy;
    private readonly ICallGateSubscriber<IGameObject?, string>? _glamourerGetAllCustomizationLegacy;
    private readonly ICallGateSubscriber<ICharacter?, uint, object?> _glamourerRevertLegacy;
    private readonly Glamourer.Api.IpcSubscribers.Legacy.RevertLock _glamourerRevertByNameLegacy;
    private readonly Glamourer.Api.IpcSubscribers.Legacy.UnlockName _glamourerUnlockLegacy;
    private readonly Glamourer.Api.Helpers.EventSubscriber<int, nint, Lazy<string>>? _glamourerStateChangedLegacy;

    private bool _useLegacyPenumbraApi = false;

    private readonly Penumbra.Api.IpcSubscribers.Legacy.AddTemporaryMod _penumbraAddTemporaryModLegacy;
    private readonly Penumbra.Api.IpcSubscribers.Legacy.RemoveTemporaryMod _penumbraRemoveTemporaryModLegacy;
    private readonly Penumbra.Api.IpcSubscribers.Legacy.CreateNamedTemporaryCollection _penumbraCreateNamedTemporaryCollectionLegacy;
    private readonly Penumbra.Api.IpcSubscribers.Legacy.AssignTemporaryCollection _penumbraAssignTemporaryCollectionLegacy;
    private readonly Penumbra.Api.IpcSubscribers.Legacy.RemoveTemporaryCollectionByName _penumbraRemoveTemporaryCollectionLegacy;
    private readonly Penumbra.Api.IpcSubscribers.Legacy.RedrawObjectByIndex _penumbraRedrawLegacy;
    private readonly Penumbra.Api.IpcSubscribers.Legacy.GetGameObjectResourcePaths _penumbraResourcePathsLegacy;

    public IpcManager(ILogger<IpcManager> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil, MareMediator mediator) : base(logger, mediator)
    {
        _pi = pi;
        _dalamudUtil = dalamudUtil;

        _penumbraInit = Penumbra.Api.IpcSubscribers.Initialized.Subscriber(pi, PenumbraInit);
        _penumbraDispose = Penumbra.Api.IpcSubscribers.Disposed.Subscriber(pi, PenumbraDispose);
        _penumbraResolveModDir = new(pi);
        _penumbraRedraw = new(pi);
        _penumbraObjectIsRedrawn = Penumbra.Api.IpcSubscribers.GameObjectRedrawn.Subscriber(pi, RedrawEvent);
        _penumbraGetMetaManipulations = new(pi);
        _penumbraRemoveTemporaryMod = new(pi);
        _penumbraAddTemporaryMod = new(pi);
        _penumbraCreateNamedTemporaryCollection = new(pi);
        _penumbraRemoveTemporaryCollection = new(pi);
        _penumbraAssignTemporaryCollection = new(pi);
        _penumbraResolvePaths = new(pi);
        _penumbraEnabled = new(pi);
        _penumbraModSettingChanged = Penumbra.Api.IpcSubscribers.ModSettingChanged.Subscriber(pi, (change, arg1, arg, b) =>
        {
            if (change == ModSettingChange.EnableState)
                Mediator.Publish(new PenumbraModSettingChangedMessage());
        });
        _penumbraConvertTextureFile = new(pi);
        _penumbraResourcePaths = new(pi);
        _penumbraApiVersion = new(pi);

        _penumbraAddTemporaryModLegacy = new(pi);
        _penumbraAssignTemporaryCollectionLegacy = new(pi);
        _penumbraCreateNamedTemporaryCollectionLegacy = new(pi);
        _penumbraRemoveTemporaryCollectionLegacy = new(pi);
        _penumbraRedrawLegacy = new(pi);
        _penumbraRemoveTemporaryModLegacy = new(pi);
        _penumbraResourcePathsLegacy = new(pi);

        _penumbraGameObjectResourcePathResolved = Penumbra.Api.IpcSubscribers.GameObjectResourcePathResolved.Subscriber(pi, ResourceLoaded);

        _glamourerApiVersions = new(pi);
        _glamourerGetAllCustomization = new(pi);
        _glamourerApplyAll = new(pi);
        _glamourerRevert = new(pi);
        _glamourerRevertByName = new(pi);
        _glamourerUnlock = new(pi);
        _glamourerUnlockByName = new(pi);

        _glamourerStateChanged = Glamourer.Api.IpcSubscribers.StateChanged.Subscriber(pi, GlamourerChanged);
        _glamourerStateChanged.Enable();

        _glamourerApiVersionLegacy = new(pi);
        _glamourerApplyAllLegacy = pi.GetIpcSubscriber<string, IGameObject?, uint, object>("Glamourer.ApplyAllToCharacterLock");
        _glamourerGetAllCustomizationLegacy = pi.GetIpcSubscriber<IGameObject?, string>("Glamourer.GetAllCustomizationFromCharacter");
        _glamourerRevertLegacy = pi.GetIpcSubscriber<ICharacter?, uint, object?>("Glamourer.RevertCharacterLock");
        _glamourerRevertByNameLegacy = new(pi);
        _glamourerUnlockLegacy = new(pi);

        _heelsGetApiVersion = pi.GetIpcSubscriber<(int, int)>("SimpleHeels.ApiVersion");
        _heelsGetOffset = pi.GetIpcSubscriber<string>("SimpleHeels.GetLocalPlayer");
        _heelsRegisterPlayer = pi.GetIpcSubscriber<int, string, object?>("SimpleHeels.RegisterPlayer");
        _heelsUnregisterPlayer = pi.GetIpcSubscriber<int, object?>("SimpleHeels.UnregisterPlayer");
        _heelsOffsetUpdate = pi.GetIpcSubscriber<string, object?>("SimpleHeels.LocalChanged");

        _heelsOffsetUpdate.Subscribe(HeelsOffsetChange);

        _customizePlusApiVersion = pi.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
        _customizePlusGetActiveProfile = pi.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
        _customizePlusGetProfileById = pi.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
        _customizePlusRevertCharacter = pi.GetIpcSubscriber<ushort, int>("CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter");
        _customizePlusSetBodyScaleToCharacter = pi.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
        _customizePlusOnScaleUpdate = pi.GetIpcSubscriber<ushort, Guid, object>("CustomizePlus.Profile.OnUpdate");
        _customizePlusDeleteByUniqueId = pi.GetIpcSubscriber<Guid, int>("CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId");

        _customizePlusOnScaleUpdate.Subscribe(OnCustomizePlusScaleChange);

        _honorificApiVersion = pi.GetIpcSubscriber<(uint, uint)>("Honorific.ApiVersion");
        _honorificGetLocalCharacterTitle = pi.GetIpcSubscriber<string>("Honorific.GetLocalCharacterTitle");
        _honorificClearCharacterTitle = pi.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");
        _honorificSetCharacterTitle = pi.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        _honorificLocalCharacterTitleChanged = pi.GetIpcSubscriber<string, object>("Honorific.LocalCharacterTitleChanged");
        _honorificDisposing = pi.GetIpcSubscriber<object>("Honorific.Disposing");
        _honorificReady = pi.GetIpcSubscriber<object>("Honorific.Ready");

        _honorificLocalCharacterTitleChanged.Subscribe(OnHonorificLocalCharacterTitleChanged);
        _honorificDisposing.Subscribe(OnHonorificDisposing);
        _honorificReady.Subscribe(OnHonorificReady);

        if (Initialized)
        {
            Mediator.Publish(new PenumbraInitializedMessage());
        }

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => PeriodicApiStateCheck());

        try
        {
            PeriodicApiStateCheck();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check for some IPC, plugin not installed?");
        }

        if (_useLegacyGlamourer)
        {
            _glamourerStateChangedLegacy = Glamourer.Api.IpcSubscribers.Legacy.StateChanged.Subscriber(pi, (t, a, c) => GlamourerChanged(a));
            _glamourerStateChangedLegacy.Enable();
        }
        else
        {
            _glamourerStateChanged = StateChanged.Subscriber(pi, GlamourerChanged);
            _glamourerStateChanged.Enable();
        }
    }

    public bool Initialized => CheckPenumbraApiInternal() && CheckGlamourerApiInternal();
    public string? PenumbraModDirectory { get; private set; }

    public bool CheckCustomizePlusApi() => _customizePlusAvailable;

    public bool CheckGlamourerApi() => _glamourerAvailable;

    public bool CheckHeelsApi() => _heelsAvailable;

    public bool CheckHonorificApi() => _honorificAvailable;

    public bool CheckPenumbraApi() => _penumbraAvailable;

    public async Task CustomizePlusRevertAsync(IntPtr character)
    {
        if (!CheckCustomizePlusApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is ICharacter c)
            {
                Logger.LogTrace("CustomizePlus reverting for {chara}", c.Address.ToString("X"));
                _customizePlusRevertCharacter!.InvokeFunc(c.ObjectIndex);
            }
        }).ConfigureAwait(false);
    }

    public async Task CustomizePlusRevertByIdAsync(Guid? profileId)
    {
        if (!CheckCustomizePlusApi() || profileId == null) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            _ = _customizePlusDeleteByUniqueId.InvokeFunc(profileId.Value);
        }).ConfigureAwait(false);
    }

    public async Task<Guid?> CustomizePlusSetBodyScaleAsync(IntPtr character, string scale)
    {
        if (!CheckCustomizePlusApi()) return null;
        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is ICharacter c)
            {
                string decodedScale = Encoding.UTF8.GetString(Convert.FromBase64String(scale));
                Logger.LogTrace("CustomizePlus applying for {chara}", c.Address.ToString("X"));
                if (scale.IsNullOrEmpty())
                {
                    _customizePlusRevertCharacter!.InvokeFunc(c.ObjectIndex);
                    return null;
                }
                else
                {
                    var result = _customizePlusSetBodyScaleToCharacter!.InvokeFunc(c.ObjectIndex, decodedScale);
                    return result.Item2;
                }
            }

            return null;
        }).ConfigureAwait(false);
    }

    public async Task<string?> GetCustomizePlusScaleAsync(IntPtr character)
    {
        if (!CheckCustomizePlusApi()) return null;
        var scale = await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is ICharacter c)
            {
                var res = _customizePlusGetActiveProfile.InvokeFunc(c.ObjectIndex);
                Logger.LogTrace("CustomizePlus GetActiveProfile returned {err}", res.Item1);
                if (res.Item1 != 0 || res.Item2 == null) return string.Empty;
                return _customizePlusGetProfileById.InvokeFunc(res.Item2.Value).Item2;
            }

            return string.Empty;
        }).ConfigureAwait(false);
        if (string.IsNullOrEmpty(scale)) return string.Empty;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(scale));
    }

    public async Task<string> GetHeelsOffsetAsync()
    {
        if (!CheckHeelsApi()) return string.Empty;
        return await _dalamudUtil.RunOnFrameworkThread(_heelsGetOffset.InvokeFunc).ConfigureAwait(false);
    }

    public async Task GlamourerApplyAllAsync(ILogger logger, GameObjectHandler handler, string? customization, Guid applicationId, CancellationToken token, bool fireAndForget = false)
    {
        if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization) || _dalamudUtil.IsZoning) return;
        try
        {
            await _redrawSemaphore.WaitAsync(token).ConfigureAwait(false);

            await PenumbraRedrawInternalAsync(logger, handler, applicationId, (chara) =>
            {
                try
                {
                    logger.LogDebug("[{appid}] Calling on IPC: GlamourerApplyAll", applicationId);
                    if (_useLegacyGlamourer)
                    {
                        _glamourerApplyAllLegacy.InvokeAction(customization, chara, LockCode);
                    }
                    else
                    {
                        _glamourerApplyAll!.Invoke(customization, chara.ObjectIndex, LockCode);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[{appid}] Failed to apply Glamourer data", applicationId);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _redrawSemaphore.Release();
        }
    }

    public async Task<string> GlamourerGetCharacterCustomizationAsync(IntPtr character)
    {
        if (!CheckGlamourerApi()) return string.Empty;
        try
        {
            return await _dalamudUtil.RunOnFrameworkThread(() =>
            {
                var gameObj = _dalamudUtil.CreateGameObject(character);
                if (gameObj is ICharacter c)
                {
                    if (_useLegacyGlamourer)
                        return _glamourerGetAllCustomizationLegacy.InvokeFunc(c) ?? string.Empty;
                    else
                        return _glamourerGetAllCustomization!.Invoke(c.ObjectIndex).Item2 ?? string.Empty;
                }
                return string.Empty;
            }).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task GlamourerRevert(ILogger logger, string name, GameObjectHandler handler, Guid applicationId, CancellationToken token)
    {
        if ((!CheckGlamourerApi()) || _dalamudUtil.IsZoning) return;
        try
        {
            await _redrawSemaphore.WaitAsync(token).ConfigureAwait(false);
            await PenumbraRedrawInternalAsync(logger, handler, applicationId, (chara) =>
            {
                try
                {
                    if (_useLegacyGlamourer)
                    {
                        logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
                        _glamourerUnlockLegacy.Invoke(name, LockCode);
                        logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevert", applicationId);
                        _glamourerRevertLegacy.InvokeAction(chara, LockCode);
                        logger.LogDebug("[{appid}] Calling On IPC: PenumbraRedraw", applicationId);
                    }
                    else
                    {
                        logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
                        _glamourerUnlock.Invoke(chara.ObjectIndex, LockCode);
                        logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevert", applicationId);
                        _glamourerRevert.Invoke(chara.ObjectIndex, LockCode);
                        logger.LogDebug("[{appid}] Calling On IPC: PenumbraRedraw", applicationId);
                    }
                    if (_useLegacyPenumbraApi)
                        _penumbraRedrawLegacy.Invoke(chara.ObjectIndex, RedrawType.AfterGPose);
                    else
                        _penumbraRedraw.Invoke(chara.ObjectIndex, RedrawType.AfterGPose);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[{appid}] Error during GlamourerRevert", applicationId);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _redrawSemaphore.Release();
        }
    }

    public async Task GlamourerRevertByNameAsync(ILogger logger, string name, Guid applicationId)
    {
        if ((!CheckGlamourerApi()) || _dalamudUtil.IsZoning) return;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            GlamourerRevertByName(logger, name, applicationId);
        }).ConfigureAwait(false);
    }

    public void GlamourerRevertByName(ILogger logger, string name, Guid applicationId)
    {
        if ((!CheckGlamourerApi()) || _dalamudUtil.IsZoning) return;
        try
        {
            if (_useLegacyGlamourer)
            {
                logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevertByName", applicationId);
                _glamourerRevertByNameLegacy.Invoke(name, LockCode);
                logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
                _glamourerUnlockLegacy.Invoke(name, LockCode);
            }
            else
            {
                logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevertByName", applicationId);
                _glamourerRevertByName.Invoke(name, LockCode);
                logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
                _glamourerUnlockByName.Invoke(name, LockCode);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during Glamourer RevertByName");
        }
    }

    public async Task HeelsRestoreOffsetForPlayerAsync(IntPtr character)
    {
        if (!CheckHeelsApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj != null)
            {
                Logger.LogTrace("Restoring Heels data to {chara}", character.ToString("X"));
                _heelsUnregisterPlayer.InvokeAction(gameObj.ObjectIndex);
            }
        }).ConfigureAwait(false);
    }

    public async Task HeelsSetOffsetForPlayerAsync(IntPtr character, string data)
    {
        if (!CheckHeelsApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj != null)
            {
                Logger.LogTrace("Applying Heels data to {chara}", character.ToString("X"));
                _heelsRegisterPlayer.InvokeAction(gameObj.ObjectIndex, data);
            }
        }).ConfigureAwait(false);
    }

    public async Task HonorificClearTitleAsync(nint character)
    {
        if (!CheckHonorificApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is IPlayerCharacter c)
            {
                Logger.LogTrace("Honorific removing for {addr}", c.Address.ToString("X"));
                _honorificClearCharacterTitle!.InvokeAction(c.ObjectIndex);
            }
        }).ConfigureAwait(false);
    }

    public string HonorificGetTitle()
    {
        if (!CheckHonorificApi()) return string.Empty;
        string title = _honorificGetLocalCharacterTitle.InvokeFunc();
        return string.IsNullOrEmpty(title) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(title));
    }

    public async Task HonorificSetTitleAsync(IntPtr character, string honorificDataB64)
    {
        if (!CheckHonorificApi()) return;
        Logger.LogTrace("Applying Honorific data to {chara}", character.ToString("X"));
        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() =>
            {
                var gameObj = _dalamudUtil.CreateGameObject(character);
                if (gameObj is IPlayerCharacter pc)
                {
                    string honorificData = string.IsNullOrEmpty(honorificDataB64) ? string.Empty : Encoding.UTF8.GetString(Convert.FromBase64String(honorificDataB64));
                    if (string.IsNullOrEmpty(honorificData))
                    {
                        _honorificClearCharacterTitle!.InvokeAction(pc.ObjectIndex);
                    }
                    else
                    {
                        _honorificSetCharacterTitle!.InvokeAction(pc.ObjectIndex, honorificData);
                    }
                }
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Could not apply Honorific data");
        }
    }

    public async Task PenumbraAssignTemporaryCollectionAsync(ILogger logger, Guid collName, int idx)
    {
        if (!CheckPenumbraApi()) return;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var retAssign = _useLegacyPenumbraApi
                ? _penumbraAssignTemporaryCollectionLegacy.Invoke(collName.ToString(), idx, force: true)
                : _penumbraAssignTemporaryCollection.Invoke(collName, idx, forceAssignment: true);
            logger.LogTrace("Assigning Temp Collection {collName} to index {idx}, Success: {ret}", collName, idx, retAssign);
            return collName;
        }).ConfigureAwait(false);
    }

    public async Task PenumbraConvertTextureFiles(ILogger logger, Dictionary<string, string[]> textures, IProgress<(string, int)> progress, CancellationToken token)
    {
        if (!CheckPenumbraApi()) return;

        Mediator.Publish(new HaltScanMessage(nameof(PenumbraConvertTextureFiles)));
        int currentTexture = 0;
        foreach (var texture in textures)
        {
            if (token.IsCancellationRequested) break;

            progress.Report((texture.Key, ++currentTexture));

            logger.LogInformation("Converting Texture {path} to {type}", texture.Key, TextureType.Bc7Tex);
            var convertTask = _penumbraConvertTextureFile.Invoke(texture.Key, texture.Key, TextureType.Bc7Tex, mipMaps: true);
            await convertTask.ConfigureAwait(false);
            if (convertTask.IsCompletedSuccessfully && texture.Value.Any())
            {
                foreach (var duplicatedTexture in texture.Value)
                {
                    logger.LogInformation("Migrating duplicate {dup}", duplicatedTexture);
                    try
                    {
                        File.Copy(texture.Key, duplicatedTexture, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to copy duplicate {dup}", duplicatedTexture);
                    }
                }
            }
        }
        Mediator.Publish(new ResumeScanMessage(nameof(PenumbraConvertTextureFiles)));

        await _dalamudUtil.RunOnFrameworkThread(async () =>
        {
            var gameObject = await _dalamudUtil.CreateGameObjectAsync(await _dalamudUtil.GetPlayerPointerAsync().ConfigureAwait(false)).ConfigureAwait(false);
            if (_useLegacyPenumbraApi)
                _penumbraRedrawLegacy.Invoke(gameObject!.ObjectIndex, RedrawType.AfterGPose);
            else
                _penumbraRedraw.Invoke(gameObject!.ObjectIndex, RedrawType.Redraw);
        }).ConfigureAwait(false);
    }

    public async Task<Guid> PenumbraCreateTemporaryCollectionAsync(ILogger logger, string uid)
    {
        if (!CheckPenumbraApi()) return Guid.Empty;

        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            if (!_useLegacyPenumbraApi)
            {
                var collName = "Loporrit_" + uid;
                var collId = _penumbraCreateNamedTemporaryCollection.Invoke(collName);
                logger.LogTrace("Creating Temp Collection {collName}, GUID: {collId}", collName, collId);
                return collId;
            }
            else
            {
                var collid = Guid.NewGuid();
                _penumbraCreateNamedTemporaryCollectionLegacy.Invoke(collid.ToString());
                logger.LogTrace("Creating Temp Collection {collName}", collid);
                return collid;
            }
        }).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, HashSet<string>>?> PenumbraGetCharacterData(ILogger logger, GameObjectHandler handler)
    {
        if (!CheckPenumbraApi()) return null;

        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            logger.LogTrace("Calling On IPC: Penumbra.GetGameObjectResourcePaths");
            var idx = handler.GetGameObject()?.ObjectIndex;
            if (idx == null) return null;
            if (_useLegacyPenumbraApi)
            {
                IReadOnlyDictionary<string, string[]>?[] ret = _penumbraResourcePathsLegacy.Invoke(idx.Value);
                if (!ret.Any()) return null;
                return ret[0]!.ToDictionary(r => r.Key, r => new HashSet<string>(r.Value));
            }
            else
            {
                return _penumbraResourcePaths.Invoke(idx.Value)[0];
            }
        }).ConfigureAwait(false);
    }

    public string PenumbraGetMetaManipulations()
    {
        if (!CheckPenumbraApi()) return string.Empty;
        return _penumbraGetMetaManipulations.Invoke();
    }

    public async Task PenumbraRedrawAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, CancellationToken token)
    {
        if (!CheckPenumbraApi() || _dalamudUtil.IsZoning) return;
        try
        {
            await _redrawSemaphore.WaitAsync(token).ConfigureAwait(false);
            await PenumbraRedrawInternalAsync(logger, handler, applicationId, (chara) =>
            {
                logger.LogDebug("[{appid}] Calling on IPC: PenumbraRedraw", applicationId);
            if (_useLegacyPenumbraApi)
                _penumbraRedrawLegacy.Invoke(chara.ObjectIndex, RedrawType.AfterGPose);
            else
                _penumbraRedraw.Invoke(chara.ObjectIndex, RedrawType.Redraw);
            }).ConfigureAwait(false);
        }
        finally
        {
            _redrawSemaphore.Release();
        }
    }

    public async Task PenumbraRemoveTemporaryCollectionAsync(ILogger logger, Guid applicationId, Guid collId)
    {
        if (!CheckPenumbraApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            logger.LogTrace("[{applicationId}] Removing temp collection for {collId}", applicationId, collId);
            var ret2 = _useLegacyPenumbraApi
                ? _penumbraRemoveTemporaryCollectionLegacy.Invoke(collId.ToString())
                : _penumbraRemoveTemporaryCollection.Invoke(collId);
            logger.LogTrace("[{applicationId}] RemoveTemporaryCollection: {ret2}", applicationId, ret2);
        }).ConfigureAwait(false);
    }

    public async Task<(string[] forward, string[][] reverse)> PenumbraResolvePathsAsync(string[] forward, string[] reverse)
    {
        return await _penumbraResolvePaths.Invoke(forward, reverse).ConfigureAwait(false);
    }

    public async Task PenumbraSetManipulationDataAsync(ILogger logger, Guid applicationId, Guid collId, string manipulationData)
    {
        if (!CheckPenumbraApi()) return;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            logger.LogTrace("[{applicationId}] Manip: {data}", applicationId, manipulationData);
            var retAdd = _useLegacyPenumbraApi
                ? _penumbraAddTemporaryModLegacy.Invoke("MareChara_Meta", collId.ToString(), [], manipulationData, 0)
                : _penumbraAddTemporaryMod.Invoke("MareChara_Meta", collId, [], manipulationData, 0);
            logger.LogTrace("[{applicationId}] Setting temp meta mod for {collId}, Success: {ret}", applicationId, collId, retAdd);
        }).ConfigureAwait(false);
    }

    public async Task PenumbraSetTemporaryModsAsync(ILogger logger, Guid applicationId, Guid collId, Dictionary<string, string> modPaths)
    {
        if (!CheckPenumbraApi()) return;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            foreach (var mod in modPaths)
            {
                logger.LogTrace("[{applicationId}] Change: {from} => {to}", applicationId, mod.Key, mod.Value);
            }
            var retRemove = _useLegacyPenumbraApi
                ? _penumbraRemoveTemporaryModLegacy.Invoke("MareChara_Files", collId.ToString(), 0)
                : _penumbraRemoveTemporaryMod.Invoke("MareChara_Files", collId, 0);
            logger.LogTrace("[{applicationId}] Removing temp files mod for {collId}, Success: {ret}", applicationId, collId, retRemove);
            var retAdd = _useLegacyPenumbraApi
                ? _penumbraAddTemporaryModLegacy.Invoke("MareChara_Files", collId.ToString(), modPaths, string.Empty, 0)
                : _penumbraAddTemporaryMod.Invoke("MareChara_Files", collId, modPaths, string.Empty, 0);
            logger.LogTrace("[{applicationId}] Setting temp files mod for {collId}, Success: {ret}", applicationId, collId, retAdd);
        }).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _disposalCts.Cancel();

        _glamourerStateChanged?.Dispose();
        _glamourerStateChangedLegacy?.Dispose();
        _penumbraModSettingChanged.Dispose();
        _penumbraGameObjectResourcePathResolved.Dispose();
        _penumbraDispose.Dispose();
        _penumbraInit.Dispose();
        _penumbraObjectIsRedrawn.Dispose();
        _heelsOffsetUpdate.Unsubscribe(HeelsOffsetChange);
        _customizePlusOnScaleUpdate.Unsubscribe(OnCustomizePlusScaleChange);
        _honorificLocalCharacterTitleChanged.Unsubscribe(OnHonorificLocalCharacterTitleChanged);
        _honorificDisposing.Unsubscribe(OnHonorificDisposing);
        _honorificReady.Unsubscribe(OnHonorificReady);
    }

    private bool CheckCustomizePlusApiInternal()
    {
        try
        {
            var version = _customizePlusApiVersion.InvokeFunc();
            if (version.Item1 == 5 && version.Item2 >= 0) return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool CheckGlamourerApiInternal()
    {
        bool apiAvailable = false;
        try
        {
            bool versionValid = (_pi.InstalledPlugins
                .FirstOrDefault(p => string.Equals(p.InternalName, "Glamourer", StringComparison.OrdinalIgnoreCase))
                ?.Version ?? new Version(0, 0, 0, 0)) >= new Version(1, 0, 6, 1);
            try
            {
                var version = _glamourerApiVersions.Invoke();
                if (version is { Major: 1, Minor: >= 1 } && versionValid)
                {
                    apiAvailable = true;
                }
            }
            catch
            {
                var version = _glamourerApiVersionLegacy.Invoke();
                if (version is { Major: 0, Minor: >= 1 } && versionValid)
                {
                    apiAvailable = true;
                    _useLegacyGlamourer = true;
                }
            }
            _shownGlamourerUnavailable = _shownGlamourerUnavailable && !apiAvailable;

            return apiAvailable;
        }
        catch
        {
            return apiAvailable;
        }
        finally
        {
            if (!apiAvailable && !_shownGlamourerUnavailable)
            {
                _shownGlamourerUnavailable = true;
                Mediator.Publish(new NotificationMessage("Glamourer inactive", "Your Glamourer installation is not active or out of date. Update Glamourer to continue to use Loporrit.", NotificationType.Error));
            }
        }
    }

    private bool CheckHeelsApiInternal()
    {
        try
        {
            return _heelsGetApiVersion.InvokeFunc() is { Item1: 2, Item2: >= 0 };
        }
        catch
        {
            return false;
        }
    }

    private bool CheckHonorificApiInternal()
    {
        try
        {
            return _honorificApiVersion.InvokeFunc() is { Item1: 3, Item2: >= 0 };
        }
        catch
        {
            return false;
        }
    }

    private bool CheckPenumbraApiInternal()
    {
        bool penumbraAvailable = false;
        try
        {
            bool pluginFound = (_pi.InstalledPlugins
                .FirstOrDefault(p => string.Equals(p.InternalName, "Penumbra", StringComparison.OrdinalIgnoreCase))
                ?.Version ?? new Version(0, 0, 0, 0)) >= new Version(1, 0, 1, 0);
            try
            {
                _ = _penumbraApiVersion.Invoke();
                _useLegacyPenumbraApi = false;
            }
            catch
            {
                _useLegacyPenumbraApi = true;
            }
            penumbraAvailable = pluginFound && _penumbraEnabled.Invoke();
            _shownPenumbraUnavailable = _shownPenumbraUnavailable && !penumbraAvailable;
            return penumbraAvailable;
        }
        catch
        {
            return penumbraAvailable;
        }
        finally
        {
            if (!penumbraAvailable && !_shownPenumbraUnavailable)
            {
                _shownPenumbraUnavailable = true;
                Mediator.Publish(new NotificationMessage("Penumbra inactive", "Your Penumbra installation is not active or out of date. Update Penumbra and/or the Enable Mods setting in Penumbra to continue to use Loporrit.", NotificationType.Error));
            }
        }
    }

    private string? GetPenumbraModDirectoryInternal()
    {
        if (!CheckPenumbraApi()) return null;
        return _penumbraResolveModDir!.Invoke().ToLowerInvariant();
    }

    private void GlamourerChanged(nint address)
    {
        Mediator.Publish(new GlamourerChangedMessage(address));
    }

    private void HeelsOffsetChange(string offset)
    {
        Mediator.Publish(new HeelsOffsetMessage());
    }

    private void OnCustomizePlusScaleChange(ushort c, Guid g)
    {
        var obj = _dalamudUtil.GetCharacterFromObjectTableByIndex(c);
        Mediator.Publish(new CustomizePlusMessage(obj?.Name.ToString() ?? string.Empty));
    }

    private void OnHonorificDisposing()
    {
        Mediator.Publish(new HonorificMessage(string.Empty));
    }

    private void OnHonorificLocalCharacterTitleChanged(string titleJson)
    {
        string titleData = string.IsNullOrEmpty(titleJson) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(titleJson));
        Mediator.Publish(new HonorificMessage(titleData));
    }

    private void OnHonorificReady()
    {
        _honorificAvailable = CheckHonorificApiInternal();
        Mediator.Publish(new HonorificReadyMessage());
    }

    private void PenumbraDispose()
    {
        _disposalCts.Cancel();
        _disposalCts.Dispose();
        Mediator.Publish(new PenumbraDisposedMessage());
        _disposalCts = new();
    }

    private void PenumbraInit()
    {
        PenumbraModDirectory = _penumbraResolveModDir.Invoke();
        _penumbraAvailable = true;
        Mediator.Publish(new PenumbraInitializedMessage());
        if (_useLegacyPenumbraApi)
            _penumbraRedrawLegacy.Invoke(0, RedrawType.Redraw);
        else
            _penumbraRedraw!.Invoke(0, RedrawType.Redraw);
    }

    private async Task PenumbraRedrawInternalAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, Action<ICharacter> action)
    {
        Mediator.Publish(new PenumbraStartRedrawMessage(handler.Address));

        _penumbraRedrawRequests[handler.Address] = true;

        try
        {
            CancellationTokenSource cancelToken = new CancellationTokenSource();
            cancelToken.CancelAfter(TimeSpan.FromSeconds(15));
            await handler.ActOnFrameworkAfterEnsureNoDrawAsync(action, cancelToken.Token).ConfigureAwait(false);

            if (!_disposalCts.Token.IsCancellationRequested)
                await _dalamudUtil.WaitWhileCharacterIsDrawing(logger, handler, applicationId, 30000, _disposalCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _penumbraRedrawRequests[handler.Address] = false;
        }

        Mediator.Publish(new PenumbraEndRedrawMessage(handler.Address));
    }

    private void PeriodicApiStateCheck()
    {
        _glamourerAvailable = CheckGlamourerApiInternal();
        _penumbraAvailable = CheckPenumbraApiInternal();
        _heelsAvailable = CheckHeelsApiInternal();
        _customizePlusAvailable = CheckCustomizePlusApiInternal();
        _honorificAvailable = CheckHonorificApiInternal();
        PenumbraModDirectory = GetPenumbraModDirectoryInternal();
    }

    private void RedrawEvent(IntPtr objectAddress, int objectTableIndex)
    {
        bool wasRequested = false;
        if (_penumbraRedrawRequests.TryGetValue(objectAddress, out var redrawRequest) && redrawRequest)
        {
            _penumbraRedrawRequests[objectAddress] = false;
        }
        else
        {
            Mediator.Publish(new PenumbraRedrawMessage(objectAddress, objectTableIndex, wasRequested));
        }
    }

    private void ResourceLoaded(IntPtr ptr, string arg1, string arg2)
    {
        if (ptr != IntPtr.Zero && string.Compare(arg1, arg2, ignoreCase: true, System.Globalization.CultureInfo.InvariantCulture) != 0)
        {
            Mediator.Publish(new PenumbraResourceLoadMessage(ptr, arg1, arg2));
        }
    }
}