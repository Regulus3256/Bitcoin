namespace BtcApi.Infrastructure.Authentication;

public interface IDemoUserRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<DemoUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken);

    Task AddAsync(DemoUser user, CancellationToken cancellationToken);
}
