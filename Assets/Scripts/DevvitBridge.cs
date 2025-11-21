using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;
using TMPro; // If using TextMeshPro
using UnityEngine.UI; // For Image component

// This script is used to communicate with the Devvit API and update the UI with the data from the API.
// This shows how to:
// 1. Pull in the username and snoovatar image from Reddit
// 2. Pull in a level index so you can alter the Unity level based on Reddit post information
// 3. Send level completed time out of Unity back to the Reddit server so you can store your data in Redis

public class DevvitBridge : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text usernameText;
    public Image targetImage;

    public TMP_Text previousTimeText;


    // Store the fetched data
    private string currentUsername;
    private string currentPostId;

    // API Response Classes (must match JSON structure listed in src/shared/types/api.ts)
    [System.Serializable]
    public class InitResponse
    {
        public string type;
        public string postId;
        public string username;
        public string snoovatarUrl;
        public string previousTime; // will be an empty string if no previous time exists

    }


    void Start()
    {
        // Fetch initial data when the game starts
        StartCoroutine(FetchInitData());
    }

    // GET request to /api/init - Fetches username, previous time, and avatar
    public IEnumerator FetchInitData()
    {

        UnityWebRequest request = UnityWebRequest.Get("/api/init");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("Error fetching init data: " + request.error + " — this will occur when running in Unity.");

            yield break;
        }

        // Parse and store the data
        InitResponse data = JsonUtility.FromJson<InitResponse>(request.downloadHandler.text);

        // Set username
        currentUsername = data.username;
        usernameText.text = "u/" + currentUsername;

        // Store post ID
        currentPostId = data.postId;

        // Set previous time (if available)
        if (previousTimeText != null && !string.IsNullOrEmpty(data.previousTime))
        {
            previousTimeText.text = "Previous Time: " + data.previousTime + "s";
        }


        // Download avatar image
        if (!string.IsNullOrEmpty(data.snoovatarUrl))
        {
            yield return StartCoroutine(DownloadImage(data.snoovatarUrl));
        }

    }

    // Downloads and displays the user's avatar image
    IEnumerator DownloadImage(string url)
    {
        UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Error downloading image: " + request.error);
            yield break;
        }

        Texture2D texture = DownloadHandlerTexture.GetContent(request);
        if (targetImage != null && texture != null)
        {
            // Convert texture to sprite and assign to Image
            Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            targetImage.sprite = newSprite;
            targetImage.preserveAspect = true; // Prevent squishing
        }
    }

    // Sends level completion data to server. Accepts an optional callback invoked with true on success, false on failure.
    public void CompleteLevel(float completionTime, Action<bool> onComplete = null)
    {
        StartCoroutine(SendCompletionRequest(completionTime, onComplete));
    }

    [System.Serializable]
    private class LevelCompletionData
    {
        public string type;
        public string username;
        public string postId;
        public string time; // Changed to string to match server expectations
    }

    private IEnumerator SendCompletionRequest(float completionTime, Action<bool> onComplete)
    {
        // Prepare request
        UnityWebRequest request = new UnityWebRequest("/api/level-completed", "POST");

        LevelCompletionData data = new()
        {
            type = "level-completed",
            username = currentUsername,
            postId = currentPostId,
            time = completionTime.ToString("F2"), // Convert to string with 2 decimal places
        };

        string jsonData = JsonUtility.ToJson(data);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
        
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        bool success = request.result == UnityWebRequest.Result.Success;

        if (!success)
        {
            Debug.LogWarning("Error sending completion data: " + request.error + " — this will occur when running in Unity.");
        }

        // Invoke callback if provided
        try
        {
            onComplete?.Invoke(success);
        }
        catch (Exception ex)
        {
            Debug.LogError("Error invoking completion callback: " + ex.Message);
        }
    }
}