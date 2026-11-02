package com.example.randomtranslate

import android.content.ClipData
import android.content.ClipboardManager
import android.os.Bundle
import android.view.View
import android.view.inputmethod.InputMethodManager
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.google.android.material.button.MaterialButton
import com.google.android.material.card.MaterialCardView
import com.google.android.material.materialswitch.MaterialSwitch
import com.google.android.material.progressindicator.LinearProgressIndicator
import com.google.android.material.slider.Slider
import com.google.android.material.textfield.TextInputEditText
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch

class MainActivity : AppCompatActivity() {

    private lateinit var inputText: TextInputEditText
    private lateinit var stepsSlider: Slider
    private lateinit var stepsLabel: TextView
    private lateinit var backSwitch: MaterialSwitch
    private lateinit var translateButton: MaterialButton
    private lateinit var progress: LinearProgressIndicator
    private lateinit var stepsContainer: LinearLayout
    private lateinit var resultCard: MaterialCardView
    private lateinit var resultTitle: TextView
    private lateinit var resultText: TextView

    private var job: Job? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        inputText = findViewById(R.id.inputText)
        stepsSlider = findViewById(R.id.stepsSlider)
        stepsLabel = findViewById(R.id.stepsLabel)
        backSwitch = findViewById(R.id.backSwitch)
        translateButton = findViewById(R.id.translateButton)
        progress = findViewById(R.id.progress)
        stepsContainer = findViewById(R.id.stepsContainer)
        resultCard = findViewById(R.id.resultCard)
        resultTitle = findViewById(R.id.resultTitle)
        resultText = findViewById(R.id.resultText)

        updateStepsLabel()
        stepsSlider.addOnChangeListener { _, _, _ -> updateStepsLabel() }

        translateButton.setOnClickListener { startChain() }

        findViewById<MaterialButton>(R.id.copyButton).setOnClickListener {
            val cm = getSystemService(ClipboardManager::class.java)
            cm.setPrimaryClip(ClipData.newPlainText("translation", resultText.text))
            Toast.makeText(this, R.string.copied, Toast.LENGTH_SHORT).show()
        }
    }

    private val steps: Int get() = stepsSlider.value.toInt()

    private fun updateStepsLabel() {
        stepsLabel.text = "${getString(R.string.label_steps)}: $steps"
        translateButton.text = getString(R.string.btn_translate_n, steps)
    }

    private fun startChain() {
        val text = inputText.text?.toString()?.trim().orEmpty()
        if (text.isEmpty()) {
            inputText.error = getString(R.string.empty_input)
            return
        }
        hideKeyboard()
        job?.cancel()

        val totalSteps = steps
        val returnBack = backSwitch.isChecked

        stepsContainer.removeAllViews()
        resultCard.visibility = View.GONE
        translateButton.isEnabled = false
        progress.visibility = View.VISIBLE
        progress.isIndeterminate = false
        progress.max = totalSteps + if (returnBack) 1 else 0
        progress.progress = 0

        job = lifecycleScope.launch {
            try {
                var current = text
                var currentLang: String? = null
                var sourceLang: String? = null
                val used = mutableSetOf<String>()

                for (i in 1..totalSteps) {
                    val target = pickRandomLanguage(currentLang, used)
                    used += target.code
                    val res = Translator.translate(current, target.code)
                    if (i == 1) sourceLang = res.detectedSource
                    if (i == 1 && sourceLang != null) used += sourceLang
                    current = res.text
                    currentLang = target.code
                    addStep("$i. → ${target.name}", current)
                    progress.progress = i
                }

                var finalTitle = "${getString(R.string.result)} (${Translator.nameOf(currentLang)})"
                if (returnBack && sourceLang != null && !sourceLang.equals(currentLang, true)) {
                    val back = Translator.translate(current, sourceLang, currentLang ?: "auto")
                    current = back.text
                    addStep("↩ ${Translator.nameOf(sourceLang)} (обратно)", current)
                    finalTitle = "${getString(R.string.result)} (${Translator.nameOf(sourceLang)})"
                }
                progress.progress = progress.max

                resultTitle.text = finalTitle
                resultText.text = current
                resultCard.visibility = View.VISIBLE
            } catch (e: kotlinx.coroutines.CancellationException) {
                throw e
            } catch (e: Exception) {
                Toast.makeText(
                    this@MainActivity,
                    "Ошибка перевода: ${e.message ?: e.javaClass.simpleName}",
                    Toast.LENGTH_LONG
                ).show()
            } finally {
                translateButton.isEnabled = true
                progress.visibility = View.GONE
            }
        }
    }

    /** Случайный язык, отличный от текущего; по возможности — ещё не использованный. */
    private fun pickRandomLanguage(current: String?, used: Set<String>): Language {
        val fresh = Translator.LANGUAGES.filter { it.code != current && it.code !in used }
        val pool = fresh.ifEmpty { Translator.LANGUAGES.filter { it.code != current } }
        return pool.random()
    }

    private fun addStep(title: String, text: String) {
        val view = layoutInflater.inflate(R.layout.item_step, stepsContainer, false)
        view.findViewById<TextView>(R.id.stepTitle).text = title
        view.findViewById<TextView>(R.id.stepText).text = text
        stepsContainer.addView(view)
    }

    private fun hideKeyboard() {
        val imm = getSystemService(InputMethodManager::class.java)
        imm.hideSoftInputFromWindow(inputText.windowToken, 0)
    }
}
