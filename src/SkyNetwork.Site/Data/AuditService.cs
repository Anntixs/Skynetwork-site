using Dapper;

namespace SkyNetwork.Site.Data;

/// <summary>Every staff action is written here and shown in the staff area.</summary>
public sealed class AuditService(Database db)
{
    public void Log(long actor, string action, string target = "", string details = "")
    {
        using var c = db.Open();
        c.Execute("INSERT INTO audit_log (actor_cid, action, target, details, created_at) VALUES (@actor, @action, @target, @details, @now)",
            new { actor, action, target, details, now = Database.Now() });
    }

    public IReadOnlyList<AuditEntry> Recent(int limit = 200, string? target = null)
    {
        using var c = db.Open();
        return c.Query<AuditEntry>("""
            SELECT a.*, COALESCE(m.name, '') AS actor_name FROM audit_log a LEFT JOIN members m ON m.cid = a.actor_cid
            WHERE @target IS NULL OR a.target = @target
            ORDER BY a.id DESC LIMIT @limit
            """, new { limit, target }).ToList();
    }

    public static string Title(string action) => action switch
    {
        "rating" => "Рейтинг",
        "suspend" => "Блокировка",
        "unsuspend" => "Разблокировка",
        "password-reset" => "Сброс пароля",
        "roles" => "Роли",
        "note" => "Заметка",
        "event" => "Мероприятие",
        "event-delete" => "Удаление мероприятия",
        "news" => "Новость",
        "news-delete" => "Удаление новости",
        "booking-delete" => "Удаление бронирования",
        "ticket" => "Обращение",
        "training" => "Обучение",
        _ => action,
    };
}
