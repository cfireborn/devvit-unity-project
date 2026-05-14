#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GoalAssignmentTrigger))]
public class GoalAssignmentTriggerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var manual = serializedObject.FindProperty("manuallySetGoal");

        EditorGUILayout.PropertyField(manual);
        if (manual.boolValue)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("goal"));
        else
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("completionTrigger"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("generatedGoalDisplayName"));
        }

        EditorGUILayout.PropertyField(serializedObject.FindProperty("makePrimaryGoalOnReceive"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("associatedItem"));

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("onGoalAdded"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("onMadePrimaryGoal"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("onGoalCompleted"));

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("waitForEnableAnimation"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("enableGoalAnimator"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("animSpriteRenderer"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("enableGoalTrigger"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("enableAnimationDelay"));

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
