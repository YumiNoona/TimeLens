package `in`.venusapp.timelens.data

import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test

class AllowanceBatchTest {
    @Test
    fun emptyBatchAndSingleAppKeepTheSameSafetySemantics() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        UsageRepository(context, Preferences(context)).use { repository ->
            assertTrue(repository.allowanceUsages(emptySet()).isEmpty())
            val now = System.currentTimeMillis()
            val batch = repository.allowanceUsages(setOf("synthetic.absent.one", "synthetic.absent.two"), now)
            assertEquals(setOf("synthetic.absent.one", "synthetic.absent.two"), batch.keys)
            assertEquals(repository.allowanceUsage("synthetic.absent.one", now), batch["synthetic.absent.one"])
            if (!repository.hasAccess()) assertTrue(batch.values.all { it == null })
            else assertTrue(batch.values.all { it == 0L })
        }
    }
}
