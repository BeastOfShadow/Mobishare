using Microsoft.Extensions.Configuration;
using AutoMapper;
using Ganss.Xss;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Mobishare.Core.Enums.Ai;
using Mobishare.Core.Enums.Chat;
using Mobishare.Core.Models.Chats;
using Mobishare.Core.Requests.Chats.ChatMessageRequests.Commands;
using Mobishare.Core.Requests.Chats.ConversationRequests.Queries;
using Mobishare.Core.Requests.Chats.MessagePairRequests.Commands;
using Mobishare.Ai.ChatBotAIService;
using OllamaSharp;
using OllamaSharp.Models;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Mobishare.Core.Requests.Chats.ConversationRequests.Commands;
using Mobishare.Ai.ChatBotAIService.ToolExecutor.Tools.VehicleTools;
using Mobishare.Core.Services.UserContext;
using Mobishare.Core.Requests.Chats.ChatMessageRequests.Queries;
using Mobishare.Ai.ChatBotAIService.ToolExecutor.Tools.RoutingTools;
using Mobishare.Ai.ChatBotAIService.ToolExecutor.Tools;
using Microsoft.AspNetCore.Identity;
using Mobishare.Ai.ChatBotAIService.Prompts;
using System.Net.Http.Json;
using Mobishare.Core.Models.Vehicles;
using Mobishare.Core.Models.Maps;
using OllamaSharp.Models.Chat;

public class ChatHub : Hub
{
    private readonly OllamaApiClient _client;
    private readonly IOllamaService _ollamaService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatHub> _logger;
    private readonly IEmbeddingService _embeddingService;
    private readonly IKnowledgeBaseRetriever _knowledgeBaseRetriever;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IUserContextService _userContext;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly HttpClient _httpClient;
    private readonly Chat _chat;
    private static readonly Dictionary<int, Timer> _inactivityTimers = new();
    private static readonly TimeSpan InactivityLimit = TimeSpan.FromMinutes(30);

    public ChatHub
    (
        IOllamaService ollamaService,
        IHttpClientFactory httpClientFactory,
        IMediator mediator, IMapper mapper,
        ILogger<ChatHub> logger,
        IConfiguration configuration,
        IEmbeddingService embeddingService,
        IKnowledgeBaseRetriever knowledgeBaseRetriever,
        IServiceScopeFactory scopeFactory,
        IHubContext<ChatHub> hubContext,
        IUserContextService userContext,
        UserManager<IdentityUser> userManager
    )
    {
        _knowledgeBaseRetriever = knowledgeBaseRetriever ?? throw new ArgumentNullException(nameof(knowledgeBaseRetriever));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _ollamaService = ollamaService ?? throw new ArgumentNullException(nameof(ollamaService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _httpClient = httpClientFactory.CreateClient("CityApi");

        _client = new OllamaApiClient(_configuration["Ollama:Llms:DefaultUrlApiClient"]!);
        _client.SelectedModel = _configuration["Ollama:Llms:Qwen3:ModelName"]!;

        _chat = new Chat(_client)
        {
            Think = true,
            Options = new RequestOptions
            {
                MiroStat = 2,
                MiroStatEta = (float?)0.3,
                MiroStatTau = (float?)3.7,
                Temperature = (float?)0.3
            }
        };
        _userManager = userManager;
        HttpClientContext.UserManagerController = _userManager;
        HttpClientContext.Client = _client;
        HttpClientContext.Chat = _chat;
    }

    public async Task SendMessage(string conversationId, string message)
    {
        var userId = Context.UserIdentifier;

        if (userId == null)
        {
            _logger.LogWarning("User identifier is null. Cannot process message.");
            await Clients.Caller.SendAsync("ReceiveMessage", "MobishareBot", "You must be logged in to send messages.", DateTime.UtcNow.ToLocalTime().ToString("g"));
            return;
        }

        // Hub methods don't flow HttpContext through IHttpContextAccessor, so
        // stash the caller's auth cookie here for CookieForwardingHandler to pick
        // up on every "CityApi" call made for the rest of this message (this
        // instance's _httpClient, and the tool classes via HttpClientContext).
        HttpClientContext.AuthCookie = Context.GetHttpContext()?.Request.Headers.Cookie.ToString();

        int newConversationId = await ConversationAssignmentAsync(conversationId, userId);

        if (newConversationId == 0)
        {
            _logger.LogWarning("Failed to assign a conversation ID. Cannot process message.");
            await Clients.Caller.SendAsync("ReceiveMessage", "MobishareBot", "An error occurred while processing your request. Please try again later.", DateTime.UtcNow.ToLocalTime().ToString("g"));
            return;
        }

        bool answered = false;
        var sanitizer = new HtmlSanitizer();
        message = sanitizer.Sanitize(message);

        await Clients.Caller.SendAsync("ReceiveMessage", "User", message, DateTime.UtcNow.ToLocalTime().ToString("g"));

        var userEmbedResponse = await _embeddingService.CreateEmbeddingAsync(message);
        var deserializeEmbedResponse = JsonSerializer.Serialize(userEmbedResponse);

        using var scope = _scopeFactory.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var messages = await mediator.Send(new GetMessagesByConversationId(newConversationId));
        var lastMessages = messages.Take(10);
        var windowMessages = lastMessages.Select(m => $"{m.Sender}: {m.Message} - {m.CreatedAt}").ToList();

        var userResponse = await _httpClient.PostAsJsonAsync("api/ChatMessage",
            new CreateChatMessage
            {
                ConversationId = newConversationId,
                Message = message,
                Sender = MessageSenderType.User.ToString(),
                CreatedAt = DateTime.UtcNow,
                Embedding = deserializeEmbedResponse
            }
        );
        
        if(!userResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to create user message. Status Code: {StatusCode}", userResponse.StatusCode);
        }
        

        var allVehicleTypes = await _httpClient.GetFromJsonAsync<IEnumerable<VehicleType>>("api/VehicleType/AllVehicleTypes");
        var allCities = await _httpClient.GetFromJsonAsync<IEnumerable<City>>("api/City/AllCities");

        var _promptCollections = new PromptCollections();
        var prompt = _promptCollections.BuildPrompt(windowMessages, message, allVehicleTypes, allCities);

        _logger.LogInformation($"Message recieved: {message} nella conversazione {conversationId}");

        UserContext.UserId = userId;
        _logger.LogInformation($"UserContext.UserId set to: {UserContext.UserId}, lat: {UserContext.Lat}, lon: {UserContext.Lon}");
        HttpClientContext.HttpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var tools = new object[] { new ReportIssueTool(), new ReserveVehicleAsyncTool(), new RoutingPageTool() };
        var aiPromtMessage = "";
        var aiThinkMessage = "";
        
        _chat.OnThink += async (sender, thinkContent) =>
        {
            aiThinkMessage += thinkContent;
        };

        _logger.LogInformation("AI thinking: {aiThinkMessage}", aiThinkMessage);

        await foreach (var response in _chat.SendAsync(prompt, tools))
        {
            aiPromtMessage += response;
            if (!string.IsNullOrEmpty(aiPromtMessage))
                await Clients.Caller.SendAsync("ReceiveMessage", "MobishareBot", response, DateTime.UtcNow.ToLocalTime().ToString("g"));
        }

        _chat.OnThink -= async (sender, thinkContent) => { };

        _chat.OnToolResult += (sender, result) =>
        {
            _logger.LogInformation("Tool result: {result}", result);
            
        };

        var aiEmbedResponse = await _embeddingService.CreateEmbeddingAsync(aiPromtMessage);
        var deserializeAiEmbedResponse = JsonSerializer.Serialize(aiEmbedResponse);
        

        var aiResponse = await _httpClient.PostAsJsonAsync("api/ChatMessage",
            new CreateChatMessage
            {
                ConversationId = newConversationId,
                Message = aiPromtMessage,
                Sender = MessageSenderType.AiAgent.ToString(),
                CreatedAt = DateTime.UtcNow,
                Embedding = deserializeAiEmbedResponse
            }
        );
        if(!aiResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to create AI message. Status Code: {StatusCode}", aiResponse.StatusCode);
        }


        answered = true;

        if (userResponse != null && aiResponse != null)
        {
            var messagePair = await _httpClient.PostAsJsonAsync("api/MessagePair",
                new CreateMessagePair
                {
                    UserMessageId = userResponse.Content.ReadFromJsonAsync<ChatMessage>().Id,
                    AiMessageId = aiResponse.Content.ReadFromJsonAsync<ChatMessage>().Id,
                    IsForRag = false,
                    SourceType = SourceType.Chatbot.ToString(),
                    Answered = answered,
                    Language = _configuration["Ollama:Llms:Qwen3:ModelName"] ?? "default"
                }
            );
        }

        ResetInactivityTimer(newConversationId, Context.ConnectionId);

        return;
    }

    private void ResetInactivityTimer(int conversationId, string connectionId)
    {
        if (_inactivityTimers.TryGetValue(conversationId, out var existingTimer))
        {
            existingTimer.Change(InactivityLimit, Timeout.InfiniteTimeSpan);
        }
        else
        {
            var timer = new Timer(async _ =>
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var completeResponse = string.Empty;
                var sanitizer = new HtmlSanitizer();

                try
                {
                    _logger.LogInformation($"Close chat {conversationId} for inactivity.");

                    await mediator.Send(new CloseConversationById(conversationId));

                    var _promptCollections = new PromptCollections();

                    await foreach (var partialResponse in _chat.SendAsync(_promptCollections.InactivityPrompt()))
                    {
                        completeResponse += partialResponse;
                        if (completeResponse != "")
                            await Clients.Caller.SendAsync("ReceiveMessage", "MobishareBot", partialResponse, DateTime.UtcNow.ToLocalTime().ToString("g"));
                    }

                    completeResponse = sanitizer.Sanitize(completeResponse);

                    var aiEmbedResponse = await _embeddingService.CreateEmbeddingAsync(completeResponse);
                    var deserializeAiEmbedResponse = JsonSerializer.Serialize(aiEmbedResponse);

                    var aiMessage = await _httpClient.PostAsJsonAsync("api/ChatMessage",
                        new CreateChatMessage
                        {
                            ConversationId = conversationId,
                            Message = completeResponse,
                            Sender = MessageSenderType.AiAgent.ToString(),
                            CreatedAt = DateTime.UtcNow,
                            Embedding = deserializeAiEmbedResponse
                        }
                    );
                    
                    if(!aiMessage.IsSuccessStatusCode)
                    {
                        _logger.LogError("Failed to create AI inactivity message. Status Code: {StatusCode}", aiMessage.StatusCode);
                    }          


                    _logger.LogDebug("Creating MessagePair with aiId={AiId}", aiMessage?.Content.ReadFromJsonAsync<ChatMessage>().Id);

                    var createMessagePair = await _httpClient.PostAsJsonAsync("api/MessagePair",
                        new CreateMessagePair
                        {
                            AiMessageId = aiMessage.Content.ReadFromJsonAsync<ChatMessage>().Id,
                            IsForRag = false,
                            SourceType = SourceType.Chatbot.ToString(),
                            Answered = true,
                            Language = _configuration["Ollama:Llms:Qwen3:ModelName"] ?? "default"
                        }
                    );
                
                    if (_inactivityTimers.TryGetValue(conversationId, out var expiredTimer))
                    {
                        expiredTimer.Dispose();
                        _inactivityTimers.Remove(conversationId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Errore durante la chiusura automatica della conversazione {conversationId}");
                }
            }, null, InactivityLimit, Timeout.InfiniteTimeSpan);

            _inactivityTimers[conversationId] = timer;
        }
    }

    private async Task<int> ConversationAssignmentAsync(string conversationId, string userId)
    {
        var conversations = await _httpClient.GetFromJsonAsync<IEnumerable<Conversation>>($"api/Conversation/AllConversationsByUserId/{userId}");

        if (conversations == null || !conversations.Any())
        {
            _logger.LogWarning("No conversations found for user ID {UserId}. Creating a new conversation.", userId);
            var newConversation = await _httpClient.PostAsJsonAsync("api/Conversation", 
                new CreateConversation
                {
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UserId = userId
                }
            );
            conversationId = newConversation.Content.ReadFromJsonAsync<Conversation>().Id.ToString();
        }
        else
        {
            var activeConversations = false;
            var currentConversation = null as Conversation;

            foreach (var conversation in conversations)
            {
                if (conversation.IsActive)
                {
                    activeConversations = true;
                    currentConversation = conversation;
                    break;
                }
            }

            if (!activeConversations)
            {
                var newConversation = await _httpClient.PostAsJsonAsync("api/Conversation", 
                    new CreateConversation
                    {
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UserId = userId
                    }
                );
                conversationId = newConversation.Content.ReadFromJsonAsync<Conversation>().Id.ToString();
            }
            else if (currentConversation != null && currentConversation.Id.ToString() != conversationId)
            {
                _logger.LogInformation($"Switching to existing conversation {currentConversation.Id}");
                conversationId = currentConversation.Id.ToString();
            }
        }

        return int.TryParse(conversationId, out var parsedConversationId) ? parsedConversationId : 0;
    }
}
