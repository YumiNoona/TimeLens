package `in`.venusapp.timelens.ui

import android.app.Application
import androidx.lifecycle.ViewModelStore
import androidx.test.platform.app.InstrumentationRegistry
import java.time.LocalDate
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertFalse
import org.junit.Test

class RefreshRegressionTest {
    @Test
    fun themeChangeIsImmediateEvenWhenHistoryWorkHoldsTheGate() = runBlocking {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val models = ViewModelStore()
        val model = withContext(Dispatchers.Main) {
            AppModel(context.applicationContext as Application).also { models.put("test", it) }
        }
        val entered = java.util.concurrent.CountDownLatch(1)
        val release = java.util.concurrent.CountDownLatch(1)
        val previous = model.preferences.theme
        try {
            withContext(Dispatchers.Main) { model.edit {
                entered.countDown()
                release.await(10, java.util.concurrent.TimeUnit.SECONDS)
            } }
            withContext(Dispatchers.IO) { org.junit.Assert.assertTrue(entered.await(10, java.util.concurrent.TimeUnit.SECONDS)) }
            withContext(Dispatchers.Main) {
                val revision = model.state.value.revision
                model.setTheme("light")
                assertEquals("light", model.preferences.theme)
                assertEquals(revision+1, model.state.value.revision)
            }
        } finally {
            release.countDown()
            withContext(Dispatchers.Main) { model.setTheme(previous); models.clear() }
        }
    }
    @Test
    fun appearanceEditsDoNotStartAnActivityScan() = runBlocking {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val models = ViewModelStore()
        val model = withContext(Dispatchers.Main) {
            AppModel(context.applicationContext as Application).also { models.put("test", it) }
        }
        val previous = model.preferences.grayIcons
        try {
            withContext(Dispatchers.Main) {
                model.edit(refreshActivity = false) { it.preferences.grayIcons = !previous }
            }
            val result = withTimeout(10_000) { model.state.first { it.revision > 0 } }
            assertFalse(result.loading)
            assertNull(result.day)
            assertEquals(!previous, model.preferences.grayIcons)
        } finally {
            withContext(Dispatchers.IO) { model.preferences.grayIcons = previous }
            withContext(Dispatchers.Main) { models.clear() }
        }
    }

    @Test
    fun rapidDateChangesPublishOnlyTheLastRequestedDay() = runBlocking {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val models = ViewModelStore()
        val latest = LocalDate.now().minusDays(2)
        val model = withContext(Dispatchers.Main) {
            AppModel(context.applicationContext as Application).also {
                models.put("test", it)
                it.refresh(latest.minusDays(2))
                it.refresh(latest.minusDays(1))
                it.refresh(latest)
            }
        }
        try {
            val result = withTimeout(30_000) { model.state.first { !it.loading } }
            assertEquals(latest, result.date)
            assertEquals(latest, result.day?.date)
            assertNull(result.error)
        } finally {
            withContext(Dispatchers.Main) { models.clear() }
        }
    }
}
