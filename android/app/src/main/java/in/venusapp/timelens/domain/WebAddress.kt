package `in`.venusapp.timelens.domain

import java.net.IDN
import java.net.URI
import java.util.Locale

/** Persist a hostname only; never paths, query strings, fragments or credentials. */
object WebAddress {
    fun host(value: String): String? = runCatching {
        val text = value.trim()
        if (text.isEmpty() || text.length > 8192 || text.any { it.isWhitespace() }) return null
        val uri = URI(if (text.contains("://")) text else "https://$text")
        if (uri.scheme !in listOf("https", "http") || uri.rawUserInfo != null) return null
        val authority = uri.rawAuthority ?: return null
        val raw = uri.host ?: authority.substringBefore(':')
        val host = IDN.toASCII(raw.trimEnd('.')).lowercase(Locale.ROOT).removePrefix("www.")
        if (
            host.length > 253 ||
                !host.contains('.') ||
                host.split('.').any {
                    it.isEmpty() ||
                        it.length > 63 ||
                        it.startsWith('-') ||
                        it.endsWith('-') ||
                        it.any { c -> !c.isLetterOrDigit() && c != '-' }
                }
        )
            null
        else host
    }
        .getOrNull()

    fun matches(host: String, rule: String) = host == rule || host.endsWith(".$rule")

    fun rule(rules: List<BlockRule>, host: String): BlockRule? =
        rules
            .filter {
                it.enabled &&
                    it.app.startsWith("web:") &&
                    matches(host, it.app.removePrefix("web:"))
            }
            .sortedWith(
                compareByDescending<BlockRule> { it.mode == "strict" }
                    .thenByDescending { it.app.length }
            )
            .firstOrNull()
}
