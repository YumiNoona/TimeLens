package `in`.venusapp.timelens.domain

import java.time.ZonedDateTime

data class BlockRule(
    val app: String,
    val mode: String = "focus",
    val enabled: Boolean = true,
    val startMinute: Int = 9 * 60,
    val endMinute: Int = 17 * 60,
    val days: Set<Int> = (1..5).toSet(),
)

data class BlockDecision(
    val blocked: Boolean,
    val reason: String = "",
    val reevaluateInMs: Long = 60_000,
)

object BlockPolicy {
    fun evaluate(
        rule: BlockRule?,
        now: ZonedDateTime,
        focusActive: Boolean,
        usedMs: Long,
        limitMinutes: Int,
        paused: Boolean,
    ): BlockDecision {
        if (rule == null || !rule.enabled || paused) return BlockDecision(false)
        return when (rule.mode) {
            "always" -> BlockDecision(true, "This app is on your block list")
            "focus" -> BlockDecision(focusActive, "Your focus session is in progress")
            "budget" ->
                if (limitMinutes <= 0) BlockDecision(false)
                else {
                    val left = limitMinutes * 60_000L - usedMs
                    BlockDecision(
                        left <= 0,
                        "Daily app budget reached",
                        if (left <= 0) 60_000 else left.coerceIn(1000, 60_000),
                    )
                }
            "schedule" -> {
                val minute = now.hour * 60 + now.minute
                val overnight = rule.startMinute >= rule.endMinute
                val day =
                    if (overnight && minute < rule.endMinute) now.minusDays(1).dayOfWeek.value
                    else now.dayOfWeek.value
                val inWindow =
                    if (overnight) minute >= rule.startMinute || minute < rule.endMinute
                    else minute >= rule.startMinute && minute < rule.endMinute
                BlockDecision(day in rule.days && inWindow, "Your scheduled focus hours are active")
            }
            else -> BlockDecision(false)
        }
    }
}
