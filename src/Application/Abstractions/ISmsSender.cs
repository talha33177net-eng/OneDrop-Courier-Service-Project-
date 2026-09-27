using Domain.Customers;

namespace Application.Abstractions;

/// <summary>Adapter for the SMS gateway. The MVP ships a fake that logs; a real provider plugs in later.</summary>
public interface ISmsSender
{
    Task SendAsync(PhoneNumber to, string senderName, string text, CancellationToken cancellationToken = default);
}
