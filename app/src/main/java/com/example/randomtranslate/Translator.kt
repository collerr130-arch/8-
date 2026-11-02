package com.example.randomtranslate

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

data class Language(val code: String, val name: String)

data class TranslationResult(val text: String, val detectedSource: String?)

object Translator {

    /** Языки, среди которых выбирается случайный. */
    val LANGUAGES = listOf(
        Language("en", "Английский"),
        Language("ru", "Русский"),
        Language("de", "Немецкий"),
        Language("fr", "Французский"),
        Language("es", "Испанский"),
        Language("it", "Итальянский"),
        Language("pt", "Португальский"),
        Language("pl", "Польский"),
        Language("uk", "Украинский"),
        Language("be", "Белорусский"),
        Language("cs", "Чешский"),
        Language("bg", "Болгарский"),
        Language("sr", "Сербский"),
        Language("nl", "Нидерландский"),
        Language("sv", "Шведский"),
        Language("no", "Норвежский"),
        Language("da", "Датский"),
        Language("fi", "Финский"),
        Language("et", "Эстонский"),
        Language("lv", "Латышский"),
        Language("lt", "Литовский"),
        Language("hu", "Венгерский"),
        Language("ro", "Румынский"),
        Language("el", "Греческий"),
        Language("tr", "Турецкий"),
        Language("ka", "Грузинский"),
        Language("hy", "Армянский"),
        Language("az", "Азербайджанский"),
        Language("kk", "Казахский"),
        Language("uz", "Узбекский"),
        Language("ar", "Арабский"),
        Language("he", "Иврит"),
        Language("fa", "Персидский"),
        Language("hi", "Хинди"),
        Language("bn", "Бенгальский"),
        Language("th", "Тайский"),
        Language("vi", "Вьетнамский"),
        Language("id", "Индонезийский"),
        Language("zh-CN", "Китайский"),
        Language("ja", "Японский"),
        Language("ko", "Корейский"),
        Language("sw", "Суахили"),
        Language("eo", "Эсперанто"),
        Language("la", "Латынь"),
    )

    fun nameOf(code: String?): String =
        LANGUAGES.firstOrNull { it.code.equals(code, ignoreCase = true) }?.name ?: (code ?: "?")

    /** Перевод через бесплатный публичный эндпоинт Google Translate (без ключа). */
    suspend fun translate(text: String, target: String, source: String = "auto"): TranslationResult =
        withContext(Dispatchers.IO) {
            val url = URL(
                "https://translate.googleapis.com/translate_a/single" +
                    "?client=gtx&dt=t" +
                    "&sl=" + URLEncoder.encode(source, "UTF-8") +
                    "&tl=" + URLEncoder.encode(target, "UTF-8") +
                    "&q=" + URLEncoder.encode(text, "UTF-8")
            )
            val conn = (url.openConnection() as HttpURLConnection).apply {
                connectTimeout = 15_000
                readTimeout = 15_000
                setRequestProperty("User-Agent", "Mozilla/5.0")
            }
            try {
                val code = conn.responseCode
                if (code != HttpURLConnection.HTTP_OK) throw IOException("HTTP $code")
                val body = conn.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
                parse(body)
            } finally {
                conn.disconnect()
            }
        }

    private fun parse(body: String): TranslationResult {
        val root = JSONArray(body)
        val sentences = root.getJSONArray(0)
        val sb = StringBuilder()
        for (i in 0 until sentences.length()) {
            val part = sentences.optJSONArray(i) ?: continue
            if (!part.isNull(0)) sb.append(part.getString(0))
        }
        val detected = if (root.length() > 2 && !root.isNull(2)) root.optString(2) else null
        return TranslationResult(sb.toString(), detected)
    }
}
