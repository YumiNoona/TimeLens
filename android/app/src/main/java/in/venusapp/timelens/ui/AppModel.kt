package `in`.venusapp.timelens.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import `in`.venusapp.timelens.data.BlockEvent
import `in`.venusapp.timelens.data.DayUsage
import `in`.venusapp.timelens.data.HistoryReport
import `in`.venusapp.timelens.data.InstalledApp
import `in`.venusapp.timelens.data.Preferences
import `in`.venusapp.timelens.data.SiteStore
import `in`.venusapp.timelens.data.UsageRepository
import `in`.venusapp.timelens.domain.Session
import `in`.venusapp.timelens.domain.WebAddress
import `in`.venusapp.timelens.platform.AppBlockService
import `in`.venusapp.timelens.platform.BackgroundTasks
import java.time.LocalDate
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

data class UiState(
    val date: LocalDate = LocalDate.now(),
    val day: DayUsage? = null,
    val loading: Boolean = false,
    val error: String? = null,
    val revision: Int = 0,
    val history: List<Session> = emptyList(),
    val installed: List<InstalledApp> = emptyList(),
    val blocks: List<BlockEvent> = emptyList(),
    val focusCompleted: Int = 0,
    val blocksToday: Int = 0,
    val blocksWeek: Int = 0,
    val webHistory: List<Session> = emptyList(),
    val allowances: Map<String, Long?> = emptyMap(),
    val historyRange: Int = 7,
    val report: HistoryReport? = null,
    val reportLoading: Boolean = false,
    val appCategories: Map<String, String> = emptyMap(),
    val appGoals: Map<String, Int> = emptyMap(),
)

class AppModel(application: Application) : AndroidViewModel(application) {
    val preferences = Preferences(application)
    private val repository = UsageRepository(application, preferences)
    private val gate = Mutex()
    private var refreshJob: Job? = null
    private var installedCache: List<InstalledApp>? = null
    private var installedAt = 0L
    private var historyVisible = false
    private var reportCache: HistoryReport? = null
    private var reportAt = 0L
    private var reportJob: Job? = null
    private var refreshedAt = 0L
    private val mutable = MutableStateFlow(UiState())
    val state = mutable.asStateFlow()

    fun refresh(date: LocalDate = mutable.value.date, force: Boolean = false) {
        // Timer ticks must not repeatedly cancel a slow refresh before it can finish.
        if (!force && refreshJob?.isActive == true && date == mutable.value.date) return
        refreshJob?.cancel()
        mutable.value =
            mutable.value.copy(
                date = date,
                loading = true,
                error = null,
                day = mutable.value.day?.takeIf { it.date == date },
            )
        refreshJob = viewModelScope.launch {
            try {
                val result =
                    withContext(Dispatchers.IO) {
                        gate.withLock {
                            BackgroundTasks.finishFocus(getApplication())
                            val day = repository.load(date)
                            ensureActive()
                            val now = System.currentTimeMillis()
                            val installed =
                                installedCache?.takeIf {
                                    !force &&
                                        android.os.SystemClock.elapsedRealtime() - installedAt <
                                            60_000
                                }
                                    ?: repository.installedApps().also {
                                        installedCache = it
                                        installedAt = android.os.SystemClock.elapsedRealtime()
                                    }
                            val comfortRules =
                                preferences.blockRules.filter { it.mode == "comfort" }
                            val appAllowances =
                                repository.allowanceUsages(
                                    comfortRules
                                        .filterNot { it.app.startsWith("web:") }
                                        .map { it.app }
                                        .toSet(),
                                    now,
                                )
                            val webAllowances =
                                if (comfortRules.any { it.app.startsWith("web:") })
                                    SiteStore(getApplication()).use {
                                        it.read(now - 86_400_000L, now)
                                    }
                                else emptyList()
                            ensureActive()
                            UiState(
                                date = date,
                                day = day,
                                history = repository.history(date, 7),
                                appCategories =
                                    installed.associate { it.app to preferences.category(it.app) },
                                appGoals =
                                    installed.associate { it.app to preferences.limit(it.app) },
                                installed = installed,
                                blocks = repository.blockHistory(),
                                focusCompleted = repository.completedFocusToday(),
                                blocksToday = repository.blockedCount(1),
                                blocksWeek = repository.blockedCount(7),
                                webHistory = repository.websites(date, 1),
                                allowances =
                                    comfortRules.associate { rule ->
                                        rule.app to
                                            if (rule.app.startsWith("web:"))
                                                webAllowances
                                                    .filter {
                                                        WebAddress.matches(
                                                            it.app,
                                                            rule.app.removePrefix("web:"),
                                                        )
                                                    }
                                                    .sumOf { it.milliseconds }
                                            else appAllowances[rule.app]
                                    },
                            )
                        }
                    }
                ensureActive()
                refreshedAt = android.os.SystemClock.elapsedRealtime()
                mutable.value =
                    result.copy(
                        revision = mutable.value.revision,
                        historyRange = mutable.value.historyRange,
                        report = mutable.value.report,
                        reportLoading = mutable.value.reportLoading,
                    )
                if (historyVisible) loadReport(force)
            } catch (cancelled: kotlinx.coroutines.CancellationException) {
                throw cancelled
            } catch (_: Exception) {
                mutable.value =
                    mutable.value.copy(
                        loading = false,
                        error =
                            "Could not read local activity. Check usage access and try Refresh again.",
                    )
            }
        }
    }

    fun edit(refreshActivity: Boolean = true, action: (UsageRepository) -> Unit) {
        viewModelScope.launch {
            try {
                withContext(Dispatchers.IO) { gate.withLock { action(repository) } }
                if (refreshActivity) {
                    reportCache = null
                    BackgroundTasks.setup(getApplication())
                    AppBlockService.changed(getApplication())
                }
                mutable.value = mutable.value.copy(revision = mutable.value.revision + 1)
                if (refreshActivity) refresh(force = true)
            } catch (cancelled: kotlinx.coroutines.CancellationException) {
                throw cancelled
            } catch (_: Exception) {
                mutable.value =
                    mutable.value.copy(error = "Could not save this change. Please try again.")
            }
        }
    }

    fun archiveNow() {
        installedCache = null
        edit { it.forceArchive() }
    }

    fun setTheme(theme: String) {
        // apply() updates memory immediately; appearance must not wait for the history lock.
        preferences.theme = theme
        mutable.value = mutable.value.copy(revision = mutable.value.revision + 1)
    }

    fun toggleFavorite(app: String) {
        val current = preferences.favorites
        preferences.favorites = if (app in current) current - app else current + app
        mutable.value = mutable.value.copy(revision = mutable.value.revision + 1)
    }

    fun setDashboardCards(cards: List<String>) {
        preferences.cards = cards.filter { it in setOf("hourly", "apps") }.distinct()
        mutable.value = mutable.value.copy(revision = mutable.value.revision + 1)
    }

    fun setHistoryVisible(visible: Boolean) {
        if (historyVisible == visible) return
        historyVisible = visible
        if (visible) loadReport()
    }

    fun setHistoryRange(days: Int) {
        if (days !in listOf(7, 30, 90, 365) || days == mutable.value.historyRange) return
        mutable.value = mutable.value.copy(historyRange = days)
        loadReport()
    }

    fun refreshIfStale(date: LocalDate = mutable.value.date) {
        if (
            mutable.value.date == date &&
                mutable.value.day != null &&
                android.os.SystemClock.elapsedRealtime() - refreshedAt < 30_000
        )
            return
        refresh(date)
    }

    private fun loadReport(force: Boolean = false) {
        val days = mutable.value.historyRange
        val end = if (days == 365) LocalDate.now() else mutable.value.date
        val cached = reportCache?.takeIf {
            !force &&
                it.days == days &&
                it.end == end &&
                android.os.SystemClock.elapsedRealtime() - reportAt < 60_000
        }
        if (cached != null) {
            mutable.value = mutable.value.copy(report = cached, reportLoading = false)
            return
        }
        if (
            reportJob?.isActive == true &&
                !force &&
                mutable.value.reportLoading &&
                requestedReport == (end to days)
        )
            return
        reportJob?.cancel()
        requestedReport = end to days
        mutable.value = mutable.value.copy(reportLoading = true)
        reportJob = viewModelScope.launch {
            try {
                val report =
                    withContext(Dispatchers.IO) { gate.withLock { repository.report(end, days) } }
                ensureActive()
                reportCache = report
                reportAt = android.os.SystemClock.elapsedRealtime()
                val currentEnd =
                    if (mutable.value.historyRange == 365) LocalDate.now() else mutable.value.date
                if (mutable.value.historyRange == days && currentEnd == end)
                    mutable.value = mutable.value.copy(report = report, reportLoading = false)
            } catch (cancelled: kotlinx.coroutines.CancellationException) {
                throw cancelled
            } catch (_: Exception) {
                mutable.value =
                    mutable.value.copy(
                        reportLoading = false,
                        error = "Could not load this history range. Try again.",
                    )
            }
        }
    }

    private var requestedReport: Pair<LocalDate, Int>? = null

    override fun onCleared() {
        // Cancellation cannot interrupt blocking SQLite calls. Wait for the same gate before close.
        refreshJob?.cancel()
        reportJob?.cancel()
        kotlinx.coroutines.CoroutineScope(Dispatchers.IO).launch {
            gate.withLock { repository.close() }
        }
        super.onCleared()
    }
}
