using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One multiple-choice quiz question for a milestone gate.
/// </summary>
[Serializable]
public class QuizQuestion
{
    [TextArea(2, 4)]
    public string prompt;

    public List<string> choices = new List<string>();

    [Tooltip("Index into choices for the correct answer.")]
    public int correctChoiceIndex;
}

/// <summary>
/// Quiz that must be fully correct before advancing to the next milestone.
/// </summary>
[CreateAssetMenu(fileName = "QuizDefinition", menuName = "ISE/Quiz Definition")]
public class QuizDefinition : ScriptableObject
{
    public string quizId;
    public string title = "Milestone Quiz";

    [TextArea(2, 4)]
    public string introText = "Answer every question correctly to unlock the next milestone.";

    public List<QuizQuestion> questions = new List<QuizQuestion>();
}
