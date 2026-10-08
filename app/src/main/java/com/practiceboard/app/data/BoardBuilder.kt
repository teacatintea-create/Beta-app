package com.practiceboard.app.data

import java.time.LocalDate
import kotlin.random.Random

/** Собирает доску на день по настройкам: часть заданий из базы, часть — от генератора. */
object BoardBuilder {

    fun build(date: LocalDate, settings: AppSettings, recentIds: Set<String>, salt: Int): List<BoardItem> {
        val items = mutableListOf<BoardItem>()
        for (sphere in Sphere.entries) {
            val s = settings.sphere(sphere)
            if (s.count <= 0) continue
            val rnd = Random(date.toEpochDay() * 31 + sphere.ordinal * 7919L + salt * 104729L)
            val levels = s.mode.levelsFor(date)
            val suitable = TaskBase.forSphere(sphere).filter { it.difficulty.level in levels }
            // Сначала то, чего не было в последние недели, потом всё остальное.
            val fresh = suitable.filter { it.id !in recentIds }.shuffled(rnd).toMutableList()
            val seen = suitable.filter { it.id in recentIds }.shuffled(rnd).toMutableList()
            val used = mutableSetOf<String>()
            repeat(s.count) {
                val wantGenerated = rnd.nextInt(100) < settings.generatorPercent
                val fromBase = if (wantGenerated) null else (fresh.removeFirstOrNull() ?: seen.removeFirstOrNull())
                val task = fromBase ?: generateUnique(sphere, Difficulty.fromLevel(levels[rnd.nextInt(levels.size)]), rnd, used)
                used += task.id
                items += BoardItem(task, done = false)
            }
        }
        return items
    }

    private fun generateUnique(sphere: Sphere, difficulty: Difficulty, rnd: Random, used: Set<String>): PracticeTask {
        var task = TaskGenerator.generate(sphere, difficulty, rnd)
        var attempts = 0
        while (task.id in used && attempts++ < 10) task = TaskGenerator.generate(sphere, difficulty, rnd)
        return task
    }
}
