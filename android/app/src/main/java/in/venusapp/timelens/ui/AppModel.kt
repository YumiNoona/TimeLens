package `in`.venusapp.timelens.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import `in`.venusapp.timelens.data.DayUsage
import `in`.venusapp.timelens.data.Preferences
import `in`.venusapp.timelens.data.UsageRepository
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.time.LocalDate

data class UiState(val date: LocalDate = LocalDate.now(), val day: DayUsage? = null, val loading: Boolean = false, val error: String? = null, val revision: Int = 0)

class AppModel(application: Application) : AndroidViewModel(application) {
    val preferences = Preferences(application)
    private val repository = UsageRepository(application, preferences)
    private val gate = Mutex()
    private var refreshJob: Job? = null
    private val mutable = MutableStateFlow(UiState())
    val state = mutable.asStateFlow()

    fun refresh(date: LocalDate = mutable.value.date) {
        refreshJob?.cancel()
        mutable.value = mutable.value.copy(date = date, loading = true, error = null, day = mutable.value.day?.takeIf { it.date == date })
        refreshJob = viewModelScope.launch {
            try {
                val result = withContext(Dispatchers.IO) { gate.withLock { repository.load(date) } }
                mutable.value = mutable.value.copy(day = result, loading = false)
            } catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
            catch (_: Exception) { mutable.value = mutable.value.copy(loading = false, error = "Could not read local activity. Check usage access and try Refresh again.") }
        }
    }
    fun edit(action: (UsageRepository) -> Unit) {
        viewModelScope.launch {
            try {
                withContext(Dispatchers.IO) { gate.withLock { action(repository) } }
                mutable.value = mutable.value.copy(revision = mutable.value.revision + 1)
                refresh()
            } catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
            catch (_: Exception) { mutable.value = mutable.value.copy(error = "Could not save this change. Please try again.") }
        }
    }
}
