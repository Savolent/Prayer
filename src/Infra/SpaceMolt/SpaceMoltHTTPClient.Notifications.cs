using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public partial class SpaceMoltHttpClient
{
    private void QueueNotifications(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
            return;

        if (!content.TryGetProperty("notifications", out var notifications) ||
            notifications.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var notification in notifications.EnumerateArray())
        {
            if (notification.ValueKind != JsonValueKind.Object)
                continue;

            string type = TryGetString(notification, "type") ?? "notification";
            string summary = BuildNotificationSummary(notification);
            string payloadJson = notification.GetRawText();

            _pendingNotifications.Add(new GameNotification
            {
                Type = type,
                Summary = summary,
                PayloadJson = payloadJson
            });

            TrackChatNotification(notification);
        }

        if (_pendingNotifications.Count > MaxQueuedNotifications)
        {
            int removeCount = _pendingNotifications.Count - MaxQueuedNotifications;
            _pendingNotifications.RemoveRange(0, removeCount);
        }

    }

    private GameNotification[] DrainPendingNotifications(int maxCount)
    {
        if (_pendingNotifications.Count == 0)
            return Array.Empty<GameNotification>();

        var latest = _pendingNotifications
            .TakeLast(Math.Max(0, maxCount))
            .ToArray();

        _pendingNotifications.Clear();
        return latest;
    }

    private static string BuildNotificationSummary(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return "notification";

        string type = TryGetString(payload, "type") ?? "notification";
        string msgType = TryGetString(payload, "msg_type") ?? "";

        JsonElement data = payload;
        if (payload.TryGetProperty("data", out var dataElement) &&
            dataElement.ValueKind == JsonValueKind.Object)
        {
            data = dataElement;
        }
        else if (payload.TryGetProperty("payload", out var payloadElement) &&
                 payloadElement.ValueKind == JsonValueKind.Object)
        {
            data = payloadElement;
        }

        // Common SpaceMolt event envelope: { type, msg_type, data, ... }.
        if (string.Equals(msgType, "chat_message", StringComparison.OrdinalIgnoreCase))
        {
            string sender = TryGetString(data, "sender") ?? "unknown";
            string content = TryGetString(data, "content") ?? "";
            string channel = TryGetString(data, "channel") ?? "";

            if (!string.IsNullOrWhiteSpace(channel))
                return string.IsNullOrWhiteSpace(content)
                    ? $"chat({channel}): {sender}"
                    : $"chat({channel}): {sender}: {content}";

            return string.IsNullOrWhiteSpace(content)
                ? $"chat: {sender}"
                : $"chat: {sender}: {content}";
        }

        if (string.Equals(msgType, "pirate_warning", StringComparison.OrdinalIgnoreCase))
        {
            string pirate = TryGetString(data, "pirate_name") ?? "Pirate";
            string message = TryGetString(data, "message") ?? "";
            return string.IsNullOrWhiteSpace(message)
                ? $"warning: {pirate}"
                : $"warning: {pirate}: {message}";
        }

        string? topMessage = TryGetString(payload, "message");
        if (!string.IsNullOrWhiteSpace(topMessage))
            return $"{type}: {topMessage}";

        string? dataMessage = TryGetString(data, "message");
        if (!string.IsNullOrWhiteSpace(dataMessage))
            return $"{type}: {dataMessage}";

        if (string.Equals(type, "mining_yield", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(msgType, "mining_yield", StringComparison.OrdinalIgnoreCase))
        {
            string resource = TryGetString(data, "resource_id") ?? "resource";
            int? quantity = TryGetInt(data, "quantity");
            return quantity.HasValue
                ? $"mining_yield: {resource} x{quantity.Value}"
                : $"mining_yield: {resource}";
        }

        string compact = BuildCompactDetail(data, maxFields: 3);
        if (!string.IsNullOrWhiteSpace(compact))
            return $"{(string.IsNullOrWhiteSpace(msgType) ? type : msgType)}: {compact}";

        return string.IsNullOrWhiteSpace(msgType)
            ? type
            : msgType;
    }

    private void TrackChatNotification(JsonElement payload)
    {
        if (!TryParseChatMessage(payload, out var chat))
            return;

        if (!string.IsNullOrWhiteSpace(chat.MessageId) &&
            _recentChatMessages.Any(m => string.Equals(m.MessageId, chat.MessageId, StringComparison.Ordinal)))
        {
            return;
        }

        chat.SeenTick = _currentTick;
        _recentChatMessages.Add(chat);

        if (_recentChatMessages.Count > MaxChatMessages)
        {
            int removeCount = _recentChatMessages.Count - MaxChatMessages;
            _recentChatMessages.RemoveRange(0, removeCount);
        }
    }

    private static bool TryParseChatMessage(JsonElement payload, out GameChatMessage chat)
    {
        chat = new GameChatMessage();
        if (payload.ValueKind != JsonValueKind.Object)
            return false;

        string type = TryGetString(payload, "type") ?? "";
        string msgType = TryGetString(payload, "msg_type") ?? "";

        if (!string.Equals(msgType, "chat_message", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(type, "chat_message", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        JsonElement data = payload;
        if (payload.TryGetProperty("data", out var dataElement) &&
            dataElement.ValueKind == JsonValueKind.Object)
        {
            data = dataElement;
        }
        else if (payload.TryGetProperty("payload", out var payloadElement) &&
                 payloadElement.ValueKind == JsonValueKind.Object)
        {
            data = payloadElement;
        }

        chat = new GameChatMessage
        {
            MessageId = TryGetString(data, "id") ?? "",
            Channel = TryGetString(data, "channel") ?? "",
            Sender = TryGetString(data, "sender") ?? "unknown",
            Content = TryGetString(data, "content") ?? ""
        };

        return true;
    }

    private GameChatMessage[] SnapshotChatMessages(int maxCount)
    {
        if (_recentChatMessages.Count == 0 || maxCount <= 0)
            return Array.Empty<GameChatMessage>();

        return _recentChatMessages
            .TakeLast(maxCount)
            .Select(m => new GameChatMessage
            {
                MessageId = m.MessageId,
                Channel = m.Channel,
                Sender = m.Sender,
                Content = m.Content,
                SeenTick = m.SeenTick
            })
            .ToArray();
    }

    private bool ObserveTickFromPayload(JsonElement payload)
    {
        int? parsedTick = TryExtractTick(payload);
        if (!parsedTick.HasValue)
            return false;

        _currentTick = Math.Max(_currentTick, parsedTick.Value);
        return true;
    }

    private static int? TryExtractTick(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        int? topTick = TryGetInt(payload, "current_tick", "tick");
        if (topTick.HasValue)
            return topTick;

        if (payload.TryGetProperty("result", out var result) &&
            result.ValueKind == JsonValueKind.Object)
        {
            int? resultTick = TryGetInt(result, "current_tick", "tick", "arrival_tick");
            if (resultTick.HasValue)
                return resultTick;
        }

        if (payload.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object)
        {
            int? dataTick = TryGetInt(data, "current_tick", "tick");
            if (dataTick.HasValue)
                return dataTick;
        }

        return null;
    }

    private static string BuildCompactDetail(JsonElement payload, int maxFields)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return "";

        var details = new List<string>();
        foreach (var prop in payload.EnumerateObject())
        {
            if (details.Count >= maxFields)
                break;

            string? value = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(value))
                continue;

            details.Add($"{prop.Name}={value}");
        }

        return string.Join(", ", details);
    }
}
