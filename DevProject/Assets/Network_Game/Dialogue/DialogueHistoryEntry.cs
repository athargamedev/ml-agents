using System;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Project-owned conversation history record used by the dialogue service.
    /// Keeps runtime history storage independent from vendor chat message types.
    /// </summary>
    [Serializable]
    public sealed class DialogueHistoryEntry
    {
        public string role;
        public string content;

        public DialogueHistoryEntry(string role, string content)
        {
            this.role = role ?? string.Empty;
            this.content = content ?? string.Empty;
        }
    }
}
