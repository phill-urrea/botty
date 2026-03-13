using Botty.Api.Controllers;
using Botty.Api.Services;
using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.LLM.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Botty.Tools.Tests;

public class ChatControllerApprovalTests
{
    [Fact]
    public async Task Chat_CreatesApprovalTask_ForGmailSend_AndSkipsImmediateExecution()
    {
        var orchestrator = new FakeOrchestrator();
        var repository = new FakeConversationRepository();
        var feedBroadcast = new NoopFeedBroadcastService();
        var toolRegistry = new FakeToolRegistry();
        var approvalService = new FakeApprovalService();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Conversation:DefaultAdminUserId"] = Guid.NewGuid().ToString()
        }).Build();

        var controller = new ChatController(
            orchestrator,
            repository,
            feedBroadcast,
            toolRegistry,
            approvalService,
            config,
            NullLogger<ChatController>.Instance);

        var response = await controller.Chat(new ChatRequestDto
        {
            Messages =
            [
                new MessageDto
                {
                    Role = "user",
                    Content = "Send a test email."
                }
            ]
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var body = Assert.IsType<ChatResponseDto>(ok.Value);
        Assert.Contains("have not executed", body.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approve task", body.Content, StringComparison.OrdinalIgnoreCase);

        var approval = Assert.Single(approvalService.Requests);
        Assert.Equal(0, toolRegistry.ExecuteToolCallCount);
        Assert.Equal(TaskType.SendMessage, approval.Type);
        Assert.Equal(TaskAssignee.Assistant, approval.Assignee);
        Assert.Equal("ExecuteSkillTool", approval.Action.ActionType);
        Assert.Equal("gmail_send_message", approval.Action.Payload?["toolName"]);
        Assert.True(approval.ConversationId.HasValue);
        Assert.True(approval.UserId.HasValue);
        Assert.Equal("admin", approval.Source);
        Assert.Equal("default", approval.ExternalId);
        Assert.True(Guid.TryParse(approval.Action.Payload?["conversationId"], out _));
        Assert.True(Guid.TryParse(approval.Action.Payload?["userId"], out _));
        Assert.Contains("\"to\":\"mail.phill@gmail.com\"", approval.Action.Payload?["arguments"] ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_CreatesApprovalTask_ForChannelSend_AndSkipsImmediateExecution()
    {
        var orchestrator = new FakeOrchestrator(
            toolName: "channel_send_message",
            arguments: """{"channel_id":"whatsapp","chat_id":"15551234567@c.us","message":"Hello there"}""");
        var repository = new FakeConversationRepository();
        var feedBroadcast = new NoopFeedBroadcastService();
        var toolRegistry = new FakeToolRegistry();
        var approvalService = new FakeApprovalService();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Conversation:DefaultAdminUserId"] = Guid.NewGuid().ToString()
        }).Build();

        var controller = new ChatController(
            orchestrator,
            repository,
            feedBroadcast,
            toolRegistry,
            approvalService,
            config,
            NullLogger<ChatController>.Instance);

        var response = await controller.Chat(new ChatRequestDto
        {
            Messages =
            [
                new MessageDto
                {
                    Role = "user",
                    Content = "Send this on WhatsApp."
                }
            ]
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var body = Assert.IsType<ChatResponseDto>(ok.Value);
        Assert.Contains("have not executed", body.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approve task", body.Content, StringComparison.OrdinalIgnoreCase);

        var approval = Assert.Single(approvalService.Requests);
        Assert.Equal(0, toolRegistry.ExecuteToolCallCount);
        Assert.Equal(TaskType.SendMessage, approval.Type);
        Assert.Equal(TaskAssignee.Assistant, approval.Assignee);
        Assert.Equal("ExecuteSkillTool", approval.Action.ActionType);
        Assert.Equal("channel_send_message", approval.Action.Payload?["toolName"]);
    }

    private sealed class FakeOrchestrator : IConversationOrchestrator
    {
        private int _calls;
        private readonly string _toolName;
        private readonly string _arguments;

        public FakeOrchestrator(
            string toolName = "gmail_send_message",
            string arguments = """{"to":"mail.phill@gmail.com","subject":"Test","body":"Hello"}""")
        {
            _toolName = toolName;
            _arguments = arguments;
        }

        public Task<ConversationResponse> ChatAsync(ConversationRequest request, CancellationToken ct = default)
        {
            _calls++;
            if (_calls == 1)
            {
                return Task.FromResult(new ConversationResponse
                {
                    Content = string.Empty,
                    ToolCalls =
                    [
                        new LlmToolCall
                        {
                            Id = "tool-1",
                            Name = _toolName,
                            Arguments = _arguments
                        }
                    ]
                });
            }

            return Task.FromResult(new ConversationResponse
            {
                Content = "Approval request created.",
                ToolCalls = null
            });
        }

        public async IAsyncEnumerable<StreamDelta> StreamChatAsync(ConversationRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            _calls++;
            await Task.CompletedTask;
            if (_calls == 1)
            {
                yield return new StreamDelta
                {
                    Type = "tool_use",
                    ToolCall = new LlmToolCall
                    {
                        Id = "tool-1",
                        Name = _toolName,
                        Arguments = _arguments
                    }
                };
                yield return new StreamDelta { Type = "done" };
            }
            else
            {
                yield return new StreamDelta { Type = "text", Text = "Approval request created." };
                yield return new StreamDelta { Type = "done" };
            }
        }

        public Task ProcessConversationForMemoryAsync(IEnumerable<ChatMessage> messages, Guid? userId = null, string? conversationId = null, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConversationRepository : IConversationRepository
    {
        private readonly Conversation _conversation = new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Source = "admin",
            ExternalId = "default",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        public Task<Conversation> GetOrCreateAsync(string source, string? externalId, Guid userId = default, string? title = null, CancellationToken ct = default)
        {
            return Task.FromResult(_conversation);
        }

        public Task<Message> AppendMessageAsync(Guid conversationId, MessageRole role, string content, string? senderName = null, string? externalId = null, string? senderId = null, CancellationToken ct = default)
        {
            return Task.FromResult(new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = _conversation.Id,
                Role = role,
                Content = content,
                SenderId = senderId,
                CreatedAt = DateTime.UtcNow
            });
        }

        public Task<IReadOnlyList<Conversation>> ListConversationsAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<Conversation>>([_conversation]);
        }

        public Task<IReadOnlyList<Message>> GetMergedFeedAsync(DateTime? since = null, int limit = 500, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<Message>>([]);
        }

        public Task<Conversation?> GetByIdAsync(Guid conversationId, CancellationToken ct = default)
        {
            return Task.FromResult<Conversation?>(_conversation.Id == conversationId ? _conversation : null);
        }

        public Task<IReadOnlyList<Message>> GetMessagesAsync(Guid conversationId, int limit = 100, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<Message>>([]);
        }

        public Task UpdateMessageContentAsync(Guid messageId, string content, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoopFeedBroadcastService : IFeedBroadcastService
    {
        public void BroadcastNewMessage(FeedMessageDto message)
        {
        }

        public void BroadcastTypingIndicator(TypingIndicatorDto indicator)
        {
        }

        public void BroadcastAssistantDelta(AssistantDeltaDto delta)
        {
        }

        public void BroadcastAssistantDone(AssistantDoneDto done)
        {
        }
    }

    private sealed class FakeToolRegistry : IToolRegistry
    {
        public int ExecuteToolCallCount { get; private set; }

        public void Register(ITool skill)
        {
        }

        public ITool? Get(string id)
        {
            return null;
        }

        public IEnumerable<ITool> GetAll()
        {
            yield return new FakeTool("gmail_send_message");
            yield return new FakeTool("channel_send_message");
        }

        public Task<bool> IsConfiguredAsync(string skillId, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task<ToolResult> ExecuteToolAsync(string toolName, string arguments, CancellationToken ct = default)
        {
            ExecuteToolCallCount++;
            return Task.FromResult(ToolResult.Ok("""{"sent":true}"""));
        }

        public Task<ToolResult> ExecuteToolAsync(
            string toolName,
            string arguments,
            Guid? conversationId,
            Guid? taskId,
            Guid? userId,
            CancellationToken ct = default)
        {
            ExecuteToolCallCount++;
            return Task.FromResult(ToolResult.Ok("""{"sent":true}"""));
        }
    }

    private sealed class FakeTool : ITool
    {
        private readonly string _toolName;

        public FakeTool(string toolName)
        {
            _toolName = toolName;
        }

        public string Id => "gmail";
        public string Name => "Gmail";
        public string Description => "Fake";
        public ToolConfigSchema ConfigSchema => new() { ToolId = "gmail", Fields = [] };

        public Task InitializeAsync(ToolConfiguration config, CancellationToken ct = default) => Task.CompletedTask;

        public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct = default) =>
            Task.FromResult(ToolResult.Ok("{}"));

        public IEnumerable<LlmTool> GetTools()
        {
            yield return new LlmTool
            {
                Name = _toolName,
                Description = "Send",
                ParametersSchema = "{}"
            };
        }
    }

    private sealed class FakeApprovalService : IApprovalService
    {
        public List<ApprovalRequest> Requests { get; } = [];

        public Task<KanbanTask> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new KanbanTask
            {
                Id = Guid.NewGuid(),
                Title = request.Title,
                Description = request.Description,
                Type = request.Type,
                Assignee = request.Assignee,
                Priority = request.Priority,
                Lane = KanbanLane.NeedsApproval,
                ConversationId = request.ConversationId,
                UserId = request.UserId,
                Source = request.Source,
                ExternalId = request.ExternalId,
                PendingActionData = request.Action,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        public Task<KanbanTask> ApproveAsync(Guid taskId, string? comment = null, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<KanbanTask> RejectAsync(Guid taskId, string reason, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<KanbanTask>> GetPendingApprovalsAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IEnumerable<KanbanTask>>([]);
        }

        public bool RequiresApproval(TaskType taskType) => true;
    }
}
