namespace StreetCorsa.Services.Talk
{
    /// <summary>
    /// Service for generating opponent dialogue messages
    /// </summary>
    public interface ITalkService
    {
        /// <summary>
        /// Get a contextual message from an opponent
        /// </summary>
        Task<string> GetMessageAsync(TalkContext context);

        /// <summary>
        /// Check if the service is available (for AI services)
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// A line for this moment written ahead, in the background, so it is ready when the moment comes (the player's AI,
        /// <see cref="AiTalkService"/>); nothing for the game's own lines, which are always ready
        /// </summary>
        void Prepare(TalkContext context)
        {
        }
    }
}
