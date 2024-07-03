﻿using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public class NotificationService : DisposableMediatorSubscriberBase
{
    private readonly INotificationManager _notificationManager;
    private readonly IChatGui _chatGui;
    private readonly MareConfigService _configurationService;

    public NotificationService(ILogger<NotificationService> logger, MareMediator mediator, INotificationManager notificationManager, IChatGui chatGui, MareConfigService configurationService) : base(logger, mediator)
    {
        _notificationManager = notificationManager;
        _chatGui = chatGui;
        _configurationService = configurationService;

        Mediator.Subscribe<NotificationMessage>(this, ShowNotification);
    }

    private void PrintErrorChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[LoporritSync] Error: " + message);
        _chatGui.PrintError(se.BuiltString);
    }

    private void PrintInfoChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[LoporritSync] Info: ").AddItalics(message ?? string.Empty);
        _chatGui.Print(se.BuiltString);
    }

    private void PrintWarnChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[LoporritSync] ").AddUiForeground("Warning: " + (message ?? string.Empty), 31).AddUiForegroundOff();
        _chatGui.Print(se.BuiltString);
    }

    private void ShowChat(NotificationMessage msg)
    {
        switch (msg.Type)
        {
            case NotificationType.Info:
            case NotificationType.Success:
            case NotificationType.None:
                PrintInfoChat(msg.Message);
                break;

            case NotificationType.Warning:
                PrintWarnChat(msg.Message);
                break;

            case NotificationType.Error:
                PrintErrorChat(msg.Message);
                break;
        }
    }

    private void ShowNotification(NotificationMessage msg)
    {
        Logger.LogInformation("{msg}", msg.ToString());

        switch (msg.Type)
        {
            case NotificationType.Info:
            case NotificationType.Success:
            case NotificationType.None:
                ShowNotificationLocationBased(msg, _configurationService.Current.InfoNotification);
                break;

            case NotificationType.Warning:
                ShowNotificationLocationBased(msg, _configurationService.Current.WarningNotification);
                break;

            case NotificationType.Error:
                ShowNotificationLocationBased(msg, _configurationService.Current.ErrorNotification);
                break;
        }
    }

    private void ShowNotificationLocationBased(NotificationMessage msg, NotificationLocation location)
    {
        switch (location)
        {
            case NotificationLocation.Toast:
                ShowToast(msg);
                break;

            case NotificationLocation.Chat:
                ShowChat(msg);
                break;

            case NotificationLocation.Both:
                ShowToast(msg);
                ShowChat(msg);
                break;

            case NotificationLocation.Nowhere:
                break;
        }
    }

    private void ShowToast(NotificationMessage msg)
    {
        var notification = new Notification{
            Content = msg.Message ?? string.Empty,
            Title = "[LoporritSync] " + msg.Title,
            Type = msg.Type,
            InitialDuration = TimeSpan.FromMilliseconds(msg.TimeShownOnScreen)
        };
        _notificationManager.AddNotification(notification);
    }
}