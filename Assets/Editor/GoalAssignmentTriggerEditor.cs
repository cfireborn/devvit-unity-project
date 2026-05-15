#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GoalAssignmentTrigger), true)]
public class GoalAssignmentTriggerEditor : Editor
{
    static readonly HashSet<string> ActivationFieldNames = new HashSet<string>
    {
        "activationChancePercent",
        "activationDelaySeconds",
        "singleUse",
        "autoDisableAfterUse"
    };

    static readonly HashSet<string> GoalSourceFieldNames = new HashSet<string>
    {
        "manuallySetGoal",
        "goal",
        "completionTrigger",
        "generatedGoalDisplayName"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
        {
            SerializedProperty scriptProp = serializedObject.FindProperty("m_Script");
            if (scriptProp != null)
                EditorGUILayout.PropertyField(scriptProp);
        }

        DrawActivationSection();
        DrawGoalSourceSection();
        DrawRemainingSerializedProperties();

        serializedObject.ApplyModifiedProperties();
    }

    void DrawActivationSection()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Activation", EditorStyles.boldLabel);
        foreach (string name in ActivationFieldNames)
        {
            SerializedProperty p = serializedObject.FindProperty(name);
            if (p != null)
                EditorGUILayout.PropertyField(p);
        }
    }

    void DrawGoalSourceSection()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Goal source", EditorStyles.boldLabel);
        SerializedProperty manual = serializedObject.FindProperty("manuallySetGoal");
        EditorGUILayout.PropertyField(manual);
        if (manual.boolValue)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("goal"));
        else
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("completionTrigger"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("generatedGoalDisplayName"));
        }
    }

    void DrawRemainingSerializedProperties()
    {
        EditorGUILayout.Space(6f);
        SerializedProperty iterator = serializedObject.GetIterator();
        if (!iterator.NextVisible(true))
            return;

        do
        {
            if (ShouldSkipProperty(iterator))
                continue;

            EditorGUILayout.PropertyField(iterator, true);
        }
        while (iterator.NextVisible(false));
    }

    bool ShouldSkipProperty(SerializedProperty prop)
    {
        if (prop.name == "m_Script")
            return true;
        if (ActivationFieldNames.Contains(prop.name))
            return true;
        if (GoalSourceFieldNames.Contains(prop.name))
            return true;
        return false;
    }
}
#endif
