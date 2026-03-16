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
      /// <summary>
      /// Gets the role of the participant in the conversation (e.g., "user", "assistant", "system").
      /// </summary>
      public string Role { get; }
      
      /// <summary>
      /// Gets the content of the conversation entry.
      /// </summary>
      public string Content { get; }

      /// <summary>
      /// Initializes a new instance of the <see cref="DialogueHistoryEntry"/> class.
      /// </summary>
      /// <param name="role">The role of the participant in the conversation.</param>
      /// <param name="content">The content of the conversation entry.</param>
      public DialogueHistoryEntry(string role, string content)
      {
          Role = role ?? string.Empty;
          Content = content ?? string.Empty;
      }
      
      /// <summary>
      /// Validates the history entry to ensure it meets basic requirements.
      /// </summary>
      /// <returns>True if the entry is valid, false otherwise.</returns>
      public bool IsValid()
      {
          // A valid history entry should have non-empty content
          return !string.IsNullOrEmpty(Content);
      }
  }
}
