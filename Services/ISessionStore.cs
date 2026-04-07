using SshClient.Models;

namespace SshClient.Services;

public interface ISessionStore
{
    IReadOnlyList<SessionProfile> GetAll();
    void Save(SessionProfile profile);
    void Delete(Guid id);
}
