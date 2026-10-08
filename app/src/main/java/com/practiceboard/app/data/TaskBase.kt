package com.practiceboard.app.data

/** Встроенная база заданий по всем трём сферам. */
object TaskBase {
    val all: List<PracticeTask> by lazy { programmingTasks() + modelingTasks() + artTasks() }

    private val bySphere: Map<Sphere, List<PracticeTask>> by lazy { all.groupBy { it.sphere } }

    fun forSphere(sphere: Sphere): List<PracticeTask> = bySphere[sphere].orEmpty()

    fun topics(sphere: Sphere): List<String> = forSphere(sphere).map { it.topic }.distinct()
}

internal class TaskListBuilder(private val sphere: Sphere, private val prefix: String) {
    private val list = mutableListOf<PracticeTask>()

    fun t(level: Int, topic: String, minutes: Int, title: String, description: String) {
        val id = prefix + (list.size + 1).toString().padStart(3, '0')
        list += PracticeTask(id, sphere, title, description, Difficulty.fromLevel(level), topic, minutes)
    }

    fun build(): List<PracticeTask> = list
}

internal fun tasks(sphere: Sphere, prefix: String, block: TaskListBuilder.() -> Unit): List<PracticeTask> =
    TaskListBuilder(sphere, prefix).apply(block).build()
