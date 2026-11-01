using QuizApp.Api.Domain;

namespace QuizApp.Api.Data;

public static class DbSeeder
{
    public static void Seed(QuizDbContext db)
    {
        if (db.Quizzes.Any()) return;

        var now = DateTime.UtcNow;

        // Correct option positions across both quizzes: each index 0–3 is used exactly 3 times,
        // so option order never leaks the answer key.
        db.Quizzes.AddRange(
            new Quiz
            {
                Title = "LLM Fundamentals for Developers",
                Description = "Core concepts every developer should know before building on large language models.",
                CreatedAt = now,
                Questions =
                [
                    Q(1, "What is a token in the context of an LLM?",
                        "Models read and generate tokens produced by a tokenizer, usually sub-word pieces. Pricing and context limits are measured in tokens, not characters or words.",
                        correctAt: 2,
                        correct: "A chunk of text, often part of a word, that the model reads and generates",
                        "Always exactly one character",
                        "One full sentence",
                        "The API key used to authenticate requests"),
                    Q(2, "What does the context window of a model limit?",
                        "The context window is the total number of tokens (prompt plus generated output) the model can attend to in a single request. Anything beyond it is not seen.",
                        correctAt: 0,
                        correct: "The number of tokens, input plus output, the model can consider in one request",
                        "The number of requests allowed per minute",
                        "The size of the dataset the model was trained on",
                        "How many chat sessions can be open at once"),
                    Q(3, "What does lowering the temperature parameter do?",
                        "Temperature scales the sampling distribution. Lower values concentrate probability on the most likely tokens, making output more focused and repeatable.",
                        correctAt: 3,
                        correct: "Makes sampling less random, so outputs are more deterministic",
                        "Shortens the maximum response length",
                        "Reduces the price per token",
                        "Lowers GPU usage during inference"),
                    Q(4, "What is a hallucination?",
                        "A hallucination is confident, fluent output that is false or not supported by the provided sources. Grounding (e.g. RAG) and verification reduce it.",
                        correctAt: 1,
                        correct: "Fluent output that is false or unsupported by the sources",
                        "The model refusing to answer a request",
                        "A timeout while streaming a response",
                        "The model repeating the prompt back verbatim"),
                    Q(5, "What are embeddings?",
                        "Embeddings map text to vectors so that semantically similar texts are close in vector space. They power semantic search, clustering, and retrieval.",
                        correctAt: 3,
                        correct: "Numeric vectors that capture meaning, so similar texts end up close together",
                        "Compressed copies of the model's weights",
                        "Images embedded inside a prompt",
                        "Encrypted versions of user messages"),
                    Q(6, "What is the purpose of a system prompt?",
                        "The system prompt sets the role, rules, tone and constraints the model should follow for the whole conversation, separate from user turns.",
                        correctAt: 0,
                        correct: "To set the model's role, rules and behavior for the conversation",
                        "To show a message when the operating system boots",
                        "To store a log of every previous user message",
                        "To choose which model version handles the request"),
                ],
            },
            new Quiz
            {
                Title = "Building AI-Powered Applications",
                Description = "Practical patterns for shipping LLM features: retrieval, tools, evals and safety.",
                CreatedAt = now,
                Questions =
                [
                    Q(1, "What is Retrieval-Augmented Generation (RAG)?",
                        "RAG retrieves relevant documents at query time and includes them in the prompt, so answers are grounded in your data without retraining the model.",
                        correctAt: 1,
                        correct: "Retrieving relevant documents and adding them to the prompt so answers are grounded in that data",
                        "Retraining the model on every user query",
                        "Randomly augmenting outputs with generated filler text",
                        "Compressing the prompt to reduce cost"),
                    Q(2, "How does tool (function) calling work?",
                        "The model never executes code itself. It returns a structured request naming a tool and its arguments; your application runs it and sends the result back.",
                        correctAt: 3,
                        correct: "The model returns a structured request for a function you define, and your code executes it",
                        "The model runs arbitrary code on your server by itself",
                        "It is a way to call the model from a command-line tool",
                        "It is a plugin that enlarges the context window"),
                    Q(3, "Why build an eval suite for an LLM feature?",
                        "Evals are a fixed set of test cases with grading. They measure quality and catch regressions whenever prompts, models or retrieval change.",
                        correctAt: 0,
                        correct: "To measure quality on fixed test cases and catch regressions when prompts or models change",
                        "Because a single manual check before launch is enough",
                        "To read the model's internal confidence score",
                        "Only model providers can run benchmarks, so it is unnecessary"),
                    Q(4, "What is prompt injection?",
                        "Prompt injection is untrusted content (user input, web pages, documents) containing instructions that try to override the application's intended behavior.",
                        correctAt: 2,
                        correct: "Untrusted input containing instructions that try to override the app's intended behavior",
                        "Inserting variables into a prompt template",
                        "Sending prompts in parallel to reduce latency",
                        "A compiler optimization applied to prompts"),
                    Q(5, "What is the main benefit of streaming responses?",
                        "Streaming sends tokens to the client as they are generated, so users see output immediately. Total generation time is similar, but perceived latency drops a lot.",
                        correctAt: 1,
                        correct: "Tokens reach the client as they are generated, reducing perceived latency",
                        "The full response is cached and replayed later",
                        "The model processes several prompts at the same time",
                        "Responses are compressed before being sent"),
                    Q(6, "Why split documents into chunks before embedding them for RAG?",
                        "Smaller, focused chunks produce more precise embeddings, improve retrieval relevance and let several results fit within the context window.",
                        correctAt: 2,
                        correct: "Focused chunks improve retrieval precision and fit within context limits",
                        "Embedding models only accept one word at a time",
                        "Chunking encrypts the documents at rest",
                        "It removes the need for a vector database"),
                ],
            });

        db.SaveChanges();
    }

    private static Question Q(int order, string text, string explanation, int correctAt, string correct, params string[] wrong)
    {
        var texts = wrong.ToList();
        texts.Insert(correctAt, correct);
        return new Question
        {
            Order = order,
            Text = text,
            Explanation = explanation,
            Options = texts.Select((t, i) => new Option { Text = t, Order = i + 1, IsCorrect = i == correctAt }).ToList(),
        };
    }
}
