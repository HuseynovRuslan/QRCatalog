namespace QrCatalog.Application.Abstractions;

/// <summary>
/// E-poçt göndərişi. Konfiqurasiya yoxdursa implementasiya sakit noop edir —
/// bildiriş heç vaxt əsas axını (sorğunun yazılmasını) bloklamamalıdır.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(
        IReadOnlyCollection<string> to, string subject, string textBody,
        CancellationToken ct = default);
}
