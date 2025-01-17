﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SemanticKernel.IntegrationTests.TestSettings;
using Xunit;
using Xunit.Abstractions;

namespace SemanticKernel.IntegrationTests.Connectors.OpenAI;

public sealed class ChatHistoryTests : IDisposable
{
    private readonly IKernelBuilder _kernelBuilder;
    private readonly XunitLogger<Kernel> _logger;
    private readonly RedirectOutput _testOutputHelper;
    private readonly IConfigurationRoot _configuration;
    private static readonly JsonSerializerOptions s_jsonOptionsCache = new() { WriteIndented = true };
    public ChatHistoryTests(ITestOutputHelper output)
    {
        this._logger = new XunitLogger<Kernel>(output);
        this._testOutputHelper = new RedirectOutput(output);
        Console.SetOut(this._testOutputHelper);

        // Load configuration
        this._configuration = new ConfigurationBuilder()
            .AddJsonFile(path: "testsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile(path: "testsettings.development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<OpenAICompletionTests>()
            .Build();

        this._kernelBuilder = Kernel.CreateBuilder();
    }

    [Fact]
    public async Task ItSerializesAndDeserializesChatHistoryAsync()
    {
        // Arrange
        this._kernelBuilder.Services.AddSingleton<ILoggerFactory>(this._logger);
        var builder = this._kernelBuilder;
        this.ConfigureAzureOpenAIChatAsText(builder);
        builder.Plugins.AddFromType<FakePlugin>();
        var kernel = builder.Build();

        OpenAIPromptExecutionSettings settings = new() { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions };
        ChatHistory history = new();

        // Act
        history.AddUserMessage("Make me a special poem");
        var historyBeforeJson = JsonSerializer.Serialize(history.ToList(), s_jsonOptionsCache);
        var service = kernel.GetRequiredService<IChatCompletionService>();
        ChatMessageContent result = await service.GetChatMessageContentAsync(history, settings, kernel);
        history.AddUserMessage("Ok thank you");

        ChatMessageContent resultOriginalWorking = await service.GetChatMessageContentAsync(history, settings, kernel);
        var historyJson = JsonSerializer.Serialize(history, s_jsonOptionsCache);
        var historyAfterSerialization = JsonSerializer.Deserialize<ChatHistory>(historyJson);
        var exception = await Record.ExceptionAsync(() => service.GetChatMessageContentAsync(historyAfterSerialization!, settings, kernel));

        // Assert
        Assert.Null(exception);
    }

    private void ConfigureAzureOpenAIChatAsText(IKernelBuilder kernelBuilder)
    {
        var azureOpenAIConfiguration = this._configuration.GetSection("Planners:AzureOpenAI").Get<AzureOpenAIConfiguration>();

        Assert.NotNull(azureOpenAIConfiguration);
        Assert.NotNull(azureOpenAIConfiguration.ChatDeploymentName);
        Assert.NotNull(azureOpenAIConfiguration.ApiKey);
        Assert.NotNull(azureOpenAIConfiguration.Endpoint);
        Assert.NotNull(azureOpenAIConfiguration.ServiceId);

        kernelBuilder.AddAzureOpenAIChatCompletion(
            deploymentName: azureOpenAIConfiguration.ChatDeploymentName,
            modelId: azureOpenAIConfiguration.ChatModelId,
            endpoint: azureOpenAIConfiguration.Endpoint,
            apiKey: azureOpenAIConfiguration.ApiKey,
            serviceId: azureOpenAIConfiguration.ServiceId);
    }

    public class FakePlugin
    {
        [KernelFunction, Description("creates a special poem")]
        public string CreateSpecialPoem()
        {
            return "ABCDE";
        }
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            this._logger.Dispose();
            this._testOutputHelper.Dispose();
        }
    }
}
