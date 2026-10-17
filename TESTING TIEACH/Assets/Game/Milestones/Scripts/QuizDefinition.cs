using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Legacy milestone quiz data retained for existing project assets.
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
/// Legacy quiz asset type. Milestone progression no longer reads or displays it.
/// </summary>
[CreateAssetMenu(fileName = "QuizDefinition", menuName = "ISE/Quiz Definition")]
public class QuizDefinition : ScriptableObject
{
    public string quizId;
    public string title = "Milestone Quiz";

    [TextArea(2, 4)]
    public string introText = "Answer every question correctly to advance to the next milestone and reveal new research.";

    public List<QuizQuestion> questions = new List<QuizQuestion>();
}
