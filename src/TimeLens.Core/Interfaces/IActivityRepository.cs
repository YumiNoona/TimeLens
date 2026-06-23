using TimeLens.Core.Models;

namespace TimeLens.Core.Interfaces;

public interface IActivityRepository
{
    Task InitializeAsync();
    Task<long> OpenAppEventAsync(AppEvent evt);
    Task CloseAppEventAsync(long id, DateTime endTime);
    Task InsertBrowserEventAsync(BrowserEvent evt);
    Task InsertSessionEventAsync(SessionEvent evt);
    Task InsertInputActivityAsync(InputActivity activity);
    Task InsertAudioActivityAsync(AudioActivity activity);
    Task<IReadOnlyList<AppEvent>> GetTodayAppEventsAsync();
    Task<IReadOnlyList<BrowserEvent>> GetTodayBrowserEventsAsync();
    Task<string?> GetCategoryAsync(string exeName, string? domain);
    Task SetCategoryAsync(string exeName, string category);
}
