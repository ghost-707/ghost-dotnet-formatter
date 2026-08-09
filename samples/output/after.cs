using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SampleApp;

public class UserService
{
    private readonly ILogger<UserService> _logger;
    private readonly IUserRepository _repo;

    public UserService(ILogger<UserService> logger, IUserRepository repo)
    {
        _logger = logger;
        _repo = repo;
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting user {Id}", id);
        return await _repo.FindAsync(id);
    }

    public async Task<IReadOnlyList<User>> SearchAsync(string query, int limit = 20)
    {
        var users = await _repo.SearchAsync(query);
        return users
            .Where(u => u.IsActive)
            .OrderBy(u => u.Name)
            .Take(limit)
            .ToList();
    }

    public string GetDisplayName(User user) => user switch
    {
        { FirstName: not null, LastName: not null } => $"{user.FirstName} {user.LastName}",
        { Email: not null } => user.Email,
        _ => $"User #{user.Id}"
    };
}

public record User(int Id, string? FirstName, string? LastName, string? Email, bool IsActive);

public interface IUserRepository
{
    Task<User?> FindAsync(int id);
    Task<IReadOnlyList<User>> SearchAsync(string query);
}
