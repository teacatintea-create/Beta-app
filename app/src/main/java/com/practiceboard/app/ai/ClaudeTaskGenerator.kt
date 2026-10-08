package com.practiceboard.app.ai

import com.anthropic.client.okhttp.AnthropicOkHttpClient
import com.anthropic.core.JsonValue
import com.anthropic.errors.AnthropicIoException
import com.anthropic.errors.AnthropicServiceException
import com.anthropic.errors.RateLimitException
import com.anthropic.errors.UnauthorizedException
import com.anthropic.models.beta.AnthropicBeta
import com.anthropic.models.beta.messages.BetaJsonOutputFormat
import com.anthropic.models.beta.messages.BetaOutputConfig
import com.anthropic.models.beta.messages.BetaStopReason
import com.anthropic.models.beta.messages.MessageCreateParams
import com.practiceboard.app.data.Difficulty
import com.practiceboard.app.data.PracticeTask
import com.practiceboard.app.data.Sphere
import com.practiceboard.app.data.TaskSource
import org.json.JSONObject
import java.time.Duration

class AiException(message: String) : Exception(message)

data class AiModelOption(val id: String, val title: String)

/**
 * Генерация заданий через Claude (Anthropic API). Ключ вводит пользователь в настройках.
 * Вызывать только из фонового потока.
 */
class ClaudeTaskGenerator(private val apiKey: String, private val model: String) {

    fun generate(
        sphere: Sphere,
        difficulty: Difficulty,
        count: Int,
        topic: String?,
        avoidTitles: List<String>,
    ): List<PracticeTask> {
        val client = AnthropicOkHttpClient.builder()
            .apiKey(apiKey)
            .timeout(Duration.ofSeconds(120))
            .build()
        try {
            val builder = MessageCreateParams.builder()
                .model(model)
                .maxTokens(16000L)
                .system(SYSTEM_PROMPT)
                .outputConfig(
                    BetaOutputConfig.builder()
                        .effort(BetaOutputConfig.Effort.LOW)
                        .format(BetaJsonOutputFormat.builder().schema(schema()).build())
                        .build()
                )
                .addUserMessage(userPrompt(sphere, difficulty, count, topic, avoidTitles))
            if (model.startsWith("claude-opus") || model.startsWith("claude-sonnet")) {
                // Если запрос отклонят классификаторы, сервер сам повторит его на подходящей модели.
                builder.addBeta(AnthropicBeta.SERVER_SIDE_FALLBACK_2026_07_01).fallbacksDefault()
            }

            val message = client.beta().messages().create(builder.build())
            val stop = message.stopReason().orElse(null)
            if (stop == BetaStopReason.REFUSAL) throw AiException("ИИ отказался выполнять этот запрос. Попробуй другую тему.")
            if (stop == BetaStopReason.MAX_TOKENS) throw AiException("Ответ ИИ оказался слишком длинным. Попробуй попросить меньше заданий.")

            val text = message.content().mapNotNull { block -> block.text().orElse(null)?.text() }.joinToString("")
            return parse(text, sphere, difficulty)
        } catch (e: AiException) {
            throw e
        } catch (e: UnauthorizedException) {
            throw AiException("Неверный API-ключ. Проверь ключ в настройках.")
        } catch (e: RateLimitException) {
            throw AiException("Слишком много запросов. Подожди минуту и попробуй снова.")
        } catch (e: AnthropicServiceException) {
            throw AiException("Ошибка сервиса Anthropic (код ${e.statusCode()}): ${e.message}")
        } catch (e: AnthropicIoException) {
            throw AiException("Нет связи с сервером. Проверь интернет.")
        } catch (e: Exception) {
            throw AiException("Не удалось получить задания: ${e.message}")
        } finally {
            client.close()
        }
    }

    private fun parse(text: String, sphere: Sphere, difficulty: Difficulty): List<PracticeTask> {
        val arr = JSONObject(text).getJSONArray("tasks")
        val now = System.currentTimeMillis()
        return (0 until arr.length()).map { i ->
            val o = arr.getJSONObject(i)
            PracticeTask(
                id = "ai-${sphere.id}-$now-$i",
                sphere = sphere,
                title = o.getString("title").trim(),
                description = o.getString("description").trim(),
                difficulty = difficulty,
                topic = o.optString("topic").trim().ifBlank { "ИИ" },
                minutes = o.optInt("minutes", 30).coerceIn(5, 300),
                source = TaskSource.AI,
            )
        }.filter { it.title.isNotBlank() && it.description.isNotBlank() }
    }

    private fun userPrompt(
        sphere: Sphere,
        difficulty: Difficulty,
        count: Int,
        topic: String?,
        avoidTitles: List<String>,
    ): String = buildString {
        append("Сфера: ").append(sphere.title).append(".\n")
        append("Уровень: ").append(difficulty.title).append(" (").append(LEVEL_HINTS.getValue(difficulty)).append(").\n")
        append("Количество заданий: ").append(count).append(".\n")
        if (!topic.isNullOrBlank()) append("Тема или пожелание ученика: ").append(topic.trim()).append(".\n")
        if (avoidTitles.isNotEmpty()) {
            append("Не повторяй эти недавние задания: ")
            append(avoidTitles.take(20).joinToString("; "))
            append(".\n")
        }
    }

    private fun schema(): BetaJsonOutputFormat.Schema {
        val str = mapOf("type" to "string")
        val task = mapOf(
            "type" to "object",
            "properties" to mapOf(
                "title" to str,
                "description" to str,
                "topic" to str,
                "minutes" to mapOf("type" to "integer"),
            ),
            "required" to listOf("title", "description", "topic", "minutes"),
            "additionalProperties" to false,
        )
        return BetaJsonOutputFormat.Schema.builder()
            .putAdditionalProperty("type", JsonValue.from("object"))
            .putAdditionalProperty("properties", JsonValue.from(mapOf("tasks" to mapOf("type" to "array", "items" to task))))
            .putAdditionalProperty("required", JsonValue.from(listOf("tasks")))
            .putAdditionalProperty("additionalProperties", JsonValue.from(false))
            .build()
    }

    companion object {
        val MODELS = listOf(
            AiModelOption("claude-opus-5-5", "Claude Opus 5.5 — лучшее качество"),
            AiModelOption("claude-sonnet-5-5", "Claude Sonnet 5.5 — быстрее и дешевле"),
            AiModelOption("claude-haiku-5-5", "Claude Haiku 5.5 — самый дешёвый"),
        )

        private val LEVEL_HINTS = mapOf(
            Difficulty.EASY to "15–30 минут, для начинающих",
            Difficulty.MEDIUM to "40–75 минут, нужен базовый опыт",
            Difficulty.HARD to "90–150 минут, небольшой проект",
        )

        private const val SYSTEM_PROMPT =
            "Ты — наставник, который придумывает практические задания для самостоятельного обучения " +
                "программированию (Kotlin и не только), 3D-моделированию (в первую очередь Blender) и 2D-рисованию. " +
                "Каждое задание должно быть конкретным и выполнимым за указанное время, с ясным результатом, " +
                "который можно показать. Пиши по-русски. title — короткое название (до 6 слов), " +
                "description — 2–4 предложения: что сделать, ограничение или условие, на что обратить внимание. " +
                "topic — тема навыка (1–3 слова), minutes — примерное время в минутах. " +
                "Задания должны отличаться друг от друга и от недавних заданий ученика."
    }
}
