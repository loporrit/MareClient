using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Pairs;

public class Pair
{
    private readonly PairHandlerFactory _cachedPlayerFactory;
    private readonly SemaphoreSlim _creationSemaphore = new(1);
    private readonly ILogger<Pair> _logger;
    private readonly MareMediator _mediator;
    private readonly MareConfigService _mareConfig;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private CancellationTokenSource _applicationCts = new CancellationTokenSource();
    private OnlineUserIdentDto? _onlineUserIdentDto = null;
    private string? _playerName = null;

    public Pair(ILogger<Pair> logger, PairHandlerFactory cachedPlayerFactory,
        MareMediator mediator, MareConfigService mareConfig, ServerConfigurationManager serverConfigurationManager)
    {
        _logger = logger;
        _cachedPlayerFactory = cachedPlayerFactory;
        _mediator = mediator;
        _mareConfig = mareConfig;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public Dictionary<GroupFullInfoDto, GroupPairFullInfoDto> GroupPair { get; set; } = new(GroupDtoComparer.Instance);
    public bool HasCachedPlayer => CachedPlayer != null && !string.IsNullOrEmpty(CachedPlayer.PlayerName) && _onlineUserIdentDto != null;
    public bool IsOnline => CachedPlayer != null;

    public bool IsPaused => UserPair != null && UserPair.OtherPermissions.IsPaired() ? UserPair.OtherPermissions.IsPaused() || UserPair.OwnPermissions.IsPaused()
            : GroupPair.All(p => p.Key.GroupUserPermissions.IsPaused() || p.Value.GroupUserPermissions.IsPaused());

    public bool IsVisible => CachedPlayer?.IsVisible ?? false;
    public CharacterData? LastReceivedCharacterData { get; set; }
    public string? PlayerName => GetPlayerName();
    public long LastAppliedDataSize => CachedPlayer?.LastAppliedDataSize ?? -1;

    public UserData UserData => UserPair?.User ?? GroupPair.First().Value.User;

    public UserPairDto? UserPair { get; set; }

    private PairHandler? CachedPlayer { get; set; }

    public void AddContextMenu(IMenuOpenedArgs args)
    {
        if (CachedPlayer == null || (args.Target is not MenuTargetDefault target) || target.TargetObjectId != CachedPlayer.PlayerCharacterId || IsPaused) return;

        args.AddMenuItem(new MenuItem()
        {
            Name = "Open Profile",
            OnClicked = (a) => _mediator.Publish(new ProfileOpenStandaloneMessage(this)),
            PrefixColor = 559,
            PrefixChar = 'L'
        });
        args.AddMenuItem(new MenuItem()
        {
            Name = "Reapply last data",
            OnClicked = (a) => ApplyLastReceivedData(forced: true),
            PrefixColor = 559,
            PrefixChar = 'L',
        });
        if (UserPair != null)
        {
            args.AddMenuItem(new MenuItem()
            {
                Name = "Change Permissions",
                OnClicked = (a) => _mediator.Publish(new OpenPermissionWindow(this)),
                PrefixColor = 559,
                PrefixChar = 'L',
            });
            args.AddMenuItem(new MenuItem()
            {
                Name = "Cycle pause state",
                OnClicked = (a) => _mediator.Publish(new CyclePauseMessage(UserData)),
                PrefixColor = 559,
                PrefixChar = 'L',
            });
        }
    }

    public void ApplyData(OnlineUserCharaDataDto data)
    {
        _applicationCts = _applicationCts.CancelRecreate();
        LastReceivedCharacterData = data.CharaData;

        if (CachedPlayer == null)
        {
            _logger.LogDebug("Received Data for {uid} but CachedPlayer does not exist, waiting", data.User.UID);
            _ = Task.Run(async () =>
            {
                using var timeoutCts = new CancellationTokenSource();
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));
                var appToken = _applicationCts.Token;
                using var combined = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, appToken);
                while (CachedPlayer == null && !combined.Token.IsCancellationRequested)
                {
                    await Task.Delay(250, combined.Token).ConfigureAwait(false);
                }

                if (!combined.IsCancellationRequested)
                {
                    _logger.LogDebug("Applying delayed data for {uid}", data.User.UID);
                    ApplyLastReceivedData();
                }
            });
            return;
        }

        ApplyLastReceivedData();
    }

    public void ApplyLastReceivedData(bool forced = false)
    {
        if (CachedPlayer == null) return;
        if (LastReceivedCharacterData == null) return;

        CachedPlayer.ApplyCharacterData(Guid.NewGuid(), RemoveNotSyncedFiles(LastReceivedCharacterData.DeepClone())!, forced);
    }

    public void CreateCachedPlayer(OnlineUserIdentDto? dto = null)
    {
        try
        {
            _creationSemaphore.Wait();

            if (CachedPlayer != null) return;

            if (dto == null && _onlineUserIdentDto == null)
            {
                CachedPlayer?.Dispose();
                CachedPlayer = null;
                return;
            }
            if (dto != null)
            {
                _onlineUserIdentDto = dto;
            }

            CachedPlayer?.Dispose();
            CachedPlayer = _cachedPlayerFactory.Create(_onlineUserIdentDto!);
        }
        finally
        {
            _creationSemaphore.Release();
        }
    }

    public string? GetNote()
    {
        return _serverConfigurationManager.GetNoteForUid(UserData.UID);
    }

    public string? GetPlayerName()
    {
        if (CachedPlayer != null && CachedPlayer.PlayerName != null)
            return CachedPlayer.PlayerName;
        else
            return _serverConfigurationManager.GetNameForUid(UserData.UID);
    }

    public string? GetNoteOrName()
    {
        string? note = GetNote();
        if (_mareConfig.Current.ShowCharacterNames || IsVisible)
            return note ?? GetPlayerName();
        else
            return note;
    }

    public string GetPlayerNameHash()
    {
        return CachedPlayer?.PlayerNameHash ?? string.Empty;
    }

    public bool HasAnyConnection()
    {
        return UserPair != null || GroupPair.Any();
    }

    public void MarkOffline()
    {
        try
        {
            _creationSemaphore.Wait();
            _onlineUserIdentDto = null;
            LastReceivedCharacterData = null;
            var player = CachedPlayer;
            CachedPlayer = null;
            player?.Dispose();
        }
        finally
        {
            _creationSemaphore.Release();
        }
    }

    public void SetNote(string note)
    {
        _serverConfigurationManager.SetNoteForUid(UserData.UID, note);
    }

    internal void SetIsUploading()
    {
        CachedPlayer?.SetUploading();
    }

    private CharacterData? RemoveNotSyncedFiles(CharacterData? data)
    {
        _logger.LogTrace("Removing not synced files");
        if (data == null)
        {
            _logger.LogTrace("Nothing to remove");
            return data;
        }

        bool disableIndividualAnimations = UserPair != null && (UserPair.OtherPermissions.IsDisableAnimations() || UserPair.OwnPermissions.IsDisableAnimations());
        bool disableIndividualVFX = UserPair != null && (UserPair.OtherPermissions.IsDisableVFX() || UserPair.OwnPermissions.IsDisableVFX());
        bool disableGroupAnimations = GroupPair.All(pair => pair.Value.GroupUserPermissions.IsDisableAnimations() || pair.Key.GroupPermissions.IsDisableAnimations() || pair.Key.GroupUserPermissions.IsDisableAnimations());

        bool disableAnimations = (UserPair != null && disableIndividualAnimations) || (UserPair == null && disableGroupAnimations);

        bool disableIndividualSounds = UserPair != null && (UserPair.OtherPermissions.IsDisableSounds() || UserPair.OwnPermissions.IsDisableSounds());
        bool disableGroupSounds = GroupPair.All(pair => pair.Value.GroupUserPermissions.IsDisableSounds() || pair.Key.GroupPermissions.IsDisableSounds() || pair.Key.GroupUserPermissions.IsDisableSounds());
        bool disableGroupVFX = GroupPair.All(pair => pair.Value.GroupUserPermissions.IsDisableVFX() || pair.Key.GroupPermissions.IsDisableVFX() || pair.Key.GroupUserPermissions.IsDisableVFX());

        bool disableSounds = (UserPair != null && disableIndividualSounds) || (UserPair == null && disableGroupSounds);
        bool disableVFX = (UserPair != null && disableIndividualVFX) || (UserPair == null && disableGroupVFX);

        _logger.LogTrace("Disable: Sounds: {disableSounds}, Anims: {disableAnimations}, VFX: {disableVFX}",
            disableSounds, disableAnimations, disableVFX);

        if (disableAnimations || disableSounds || disableVFX)
        {
            _logger.LogTrace("Data cleaned up: Animations disabled: {disableAnimations}, Sounds disabled: {disableSounds}, VFX disabled: {disableVFX}",
                disableAnimations, disableSounds, disableVFX);
            foreach (var objectKind in data.FileReplacements.Select(k => k.Key))
            {
                if (disableSounds)
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("scd", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if (disableAnimations)
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("tmb", StringComparison.OrdinalIgnoreCase) || p.EndsWith("pap", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if (disableVFX)
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("atex", StringComparison.OrdinalIgnoreCase) || p.EndsWith("avfx", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
            }
        }

        return data;
    }
}