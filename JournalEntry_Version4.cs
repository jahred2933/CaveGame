using UnityEngine;

public class JournalEntry : MonoBehaviour
{
    public string entryID; // Set to "1", "2", "3", etc.
    [TextArea] public string entryText;
}