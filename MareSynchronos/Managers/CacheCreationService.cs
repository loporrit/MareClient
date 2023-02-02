﻿using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.Factories;
using MareSynchronos.Mediator;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Managers;

public class CacheCreationService : MediatorSubscriberBase, IDisposable
{
    private readonly CharacterDataFactory _characterDataFactory;
    private readonly IpcManager _ipcManager;
    private readonly ApiController _apiController;
    private Task? _cacheCreationTask;
    private Dictionary<ObjectKind, GameObjectHandler> _cachesToCreate = new();
    private CharacterData _lastCreatedData = new();
    private CancellationTokenSource cts = new();
    private List<GameObjectHandler> _playerRelatedObjects;

    public unsafe CacheCreationService(MareMediator mediator, CharacterDataFactory characterDataFactory, IpcManager ipcManager,
        ApiController apiController, DalamudUtil dalamudUtil) : base(mediator)
    {
        _characterDataFactory = characterDataFactory;
        _ipcManager = ipcManager;
        _apiController = apiController;

        Mediator.Subscribe<CreateCacheForObjectMessage>(this, (msg) =>
        {
            var actualMsg = (CreateCacheForObjectMessage)msg;
            _cachesToCreate[actualMsg.ObjectToCreateFor.ObjectKind] = actualMsg.ObjectToCreateFor;
        });
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (msg) => ProcessCacheCreation());
        Mediator.Subscribe<CustomizePlusMessage>(this, (msg) => CustomizePlusChanged((CustomizePlusMessage)msg));
        Mediator.Subscribe<HeelsOffsetMessage>(this, (msg) => HeelsOffsetChanged((HeelsOffsetMessage)msg));
        Mediator.Subscribe<PalettePlusMessage>(this, (msg) => PalettePlusChanged((PalettePlusMessage)msg));

        _playerRelatedObjects = new List<GameObjectHandler>()
        {
            new(Mediator, ObjectKind.Player, () => dalamudUtil.PlayerPointer),
            new(Mediator, ObjectKind.MinionOrMount, () => (IntPtr)((Character*)dalamudUtil.PlayerPointer)->CompanionObject),
            new(Mediator, ObjectKind.Pet, () => dalamudUtil.GetPet()),
            new(Mediator, ObjectKind.Companion, () => dalamudUtil.GetCompanion()),
        };
    }

    private void PalettePlusChanged(PalettePlusMessage msg)
    {
        if (!string.Equals(msg.Data, _lastCreatedData.PalettePlusPalette, StringComparison.Ordinal))
        {
            _lastCreatedData.PalettePlusPalette = msg.Data ?? string.Empty;
            Mediator.Publish(new CharacterDataCreatedMessage(_lastCreatedData));
        }
    }

    private void HeelsOffsetChanged(HeelsOffsetMessage msg)
    {
        if (msg.Offset != _lastCreatedData.HeelsOffset)
        {
            _lastCreatedData.HeelsOffset = msg.Offset;
            Mediator.Publish(new CharacterDataCreatedMessage(_lastCreatedData));
        }
    }

    private void CustomizePlusChanged(CustomizePlusMessage msg)
    {
        if (!string.Equals(msg.Data, _lastCreatedData.CustomizePlusScale, StringComparison.Ordinal))
        {
            _lastCreatedData.CustomizePlusScale = msg.Data ?? string.Empty;
            Mediator.Publish(new CharacterDataCreatedMessage(_lastCreatedData));
        }
    }

    private void ProcessCacheCreation()
    {
        if (_cachesToCreate.Any() && (_cacheCreationTask?.IsCompleted ?? true))
        {
            var toCreate = _cachesToCreate.ToList();
            _cachesToCreate.Clear();
            _cacheCreationTask = Task.Run(() =>
            {
                try
                {
                    foreach (var obj in toCreate)
                    {
                        var data = _characterDataFactory.BuildCharacterData(_lastCreatedData, obj.Value, cts.Token);
                    }
                    Mediator.Publish(new CharacterDataCreatedMessage(_lastCreatedData));
                }
                catch (Exception ex)
                {
                    Logger.Error("Error during Cache Creation Processing", ex);
                }
                finally
                {
                    Logger.Debug("Cache Creation complete");

                }
            }, cts.Token);
        }
        else if (_cachesToCreate.Any())
        {
            Logger.Debug("Cache Creation stored until previous creation finished");
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _playerRelatedObjects.ForEach(p => p.Dispose());
        cts.Dispose();
    }
}
