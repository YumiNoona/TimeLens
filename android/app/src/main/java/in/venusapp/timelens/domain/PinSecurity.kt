package `in`.venusapp.timelens.domain

import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec

object PinSecurity {
    fun hash(pin: String): String {
        require(pin.matches(Regex("[0-9]{4,12}")))
        val salt = ByteArray(16).also { SecureRandom().nextBytes(it) }
        return "${Base64.getEncoder().encodeToString(salt)}:${Base64.getEncoder().encodeToString(derive(pin, salt))}"
    }

    fun verify(pin: String, stored: String): Boolean = runCatching {
        val parts = stored.split(':')
        parts.size == 2 &&
            MessageDigest.isEqual(
                derive(pin, Base64.getDecoder().decode(parts[0])),
                Base64.getDecoder().decode(parts[1]),
            )
    }
        .getOrDefault(false)

    private fun derive(pin: String, salt: ByteArray): ByteArray {
        val key = PBEKeySpec(pin.toCharArray(), salt, 120_000, 256)
        return try {
            SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(key).encoded
        } finally {
            key.clearPassword()
        }
    }
}
