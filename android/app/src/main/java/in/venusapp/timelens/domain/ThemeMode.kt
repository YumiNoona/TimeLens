package `in`.venusapp.timelens.domain

object ThemeMode {
    val options = listOf("system", "light", "dark")
    fun normalize(value: String?) = value?.takeIf { it in options } ?: "system"
    fun isDark(value: String?, systemDark: Boolean) = when (normalize(value)) {
        "dark" -> true
        "light" -> false
        else -> systemDark
    }
}
