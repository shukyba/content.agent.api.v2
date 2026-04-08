using ContentAgent.Api.Models;

namespace ContentAgent.Api.Services;

public interface IIdeaGenerationService
{
    /// <summary>
    /// Produces social-post ideas for the topic label using Gemini, incorporating optional user notes.
    /// </summary>
    Task<GenerateIdeasResponse> GenerateAsync(string topicLabel, string userInput, CancellationToken cancellationToken);
}
