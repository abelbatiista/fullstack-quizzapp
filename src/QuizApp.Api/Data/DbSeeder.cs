using QuizApp.Api.Domain;

namespace QuizApp.Api.Data;

public static class DbSeeder
{
    public static void Seed(QuizDbContext db)
    {
        if (db.Quizzes.Any()) return;

        var now = DateTime.UtcNow;

        // The answer key must not leak through the options themselves:
        // - Position: each index 0–3 holds the correct answer exactly 3 times across both quizzes.
        // - Length/specificity: distractors are plausible, similarly long, and as specific as the
        //   correct answer, so "pick the longest option" doesn't work (guarded by a seed test).
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
                        correct: "A chunk of text, often part of a word, that the model reads and writes",
                        "A single character, since models process text one letter at a time",
                        "A whole word, because tokenizers split text only on spaces and punctuation",
                        "A credential issued per request to authenticate and meter the API caller"),
                    Q(2, "What does the context window of a model limit?",
                        "The context window is the total number of tokens (prompt plus generated output) the model can attend to in a single request. Anything beyond it is not seen.",
                        correctAt: 0,
                        correct: "The tokens, prompt plus output, that the model can consider in one request",
                        "The number of requests per minute the provider allows before rate limiting",
                        "How many earlier chat sessions the model remembers about a returning user",
                        "The training-data cutoff date, after which the model knows no new events"),
                    Q(3, "What does lowering the temperature parameter do?",
                        "Temperature scales the sampling distribution. Lower values concentrate probability on the most likely tokens, making output more focused and repeatable.",
                        correctAt: 3,
                        correct: "Makes sampling less random, so outputs are more focused and repeatable",
                        "Shortens responses by lowering the maximum number of tokens it may emit",
                        "Reduces the cost per token, because cooler sampling uses fewer GPU cycles",
                        "Makes the model more creative by flattening the probability distribution"),
                    Q(4, "What is a hallucination?",
                        "A hallucination is confident, fluent output that is false or not supported by the provided sources. Grounding (e.g. RAG) and verification reduce it.",
                        correctAt: 1,
                        correct: "Fluent, confident output that is false or unsupported by the sources",
                        "The model refusing a request because it judges the content to be unsafe",
                        "Answers that drift off-topic once the prompt exceeds the context window",
                        "A streaming glitch where tokens arrive out of order and garble the reply"),
                    Q(5, "What are embeddings?",
                        "Embeddings map text to vectors so that semantically similar texts are close in vector space. They power semantic search, clustering, and retrieval.",
                        correctAt: 3,
                        correct: "Numeric vectors that capture meaning, so similar texts end up close",
                        "Compressed copies of the model's weights that let it run on small devices",
                        "Hidden instructions embedded in a prompt to steer the model's behavior",
                        "Encrypted copies of user messages, stored so the provider cannot read them"),
                    Q(6, "What is the purpose of a system prompt?",
                        "The system prompt sets the role, rules, tone and constraints the model should follow for the whole conversation, separate from user turns.",
                        correctAt: 0,
                        correct: "To set the model's role, rules and behavior for the whole conversation",
                        "To hold the first user message, which the model treats as the chat's topic",
                        "To select which model version and region will handle incoming requests",
                        "To replay a log of earlier conversations so replies feel more personal"),
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
                        correct: "Retrieving relevant documents at query time and adding them to the prompt",
                        "Fine-tuning the model on your documents so it memorizes them before launch",
                        "Generating several answers and returning the one the model rates highest",
                        "Summarizing long prompts so that more of them fit in the context window"),
                    Q(2, "How does tool (function) calling work?",
                        "The model never executes code itself. It returns a structured request naming a tool and its arguments; your application runs it and sends the result back.",
                        correctAt: 3,
                        correct: "The model returns a structured call to a function you define; your code runs it",
                        "The model connects to your database directly and runs the queries it writes",
                        "Your code calls the model once per tool, and each call loads a separate model",
                        "The provider installs plugins that permanently extend what the model knows"),
                    Q(3, "Why build an eval suite for an LLM feature?",
                        "Evals are a fixed set of test cases with grading. They measure quality and catch regressions whenever prompts, models or retrieval change.",
                        correctAt: 0,
                        correct: "To score fixed test cases and catch regressions when prompts or models change",
                        "To read the model's internal confidence so low-certainty answers are hidden",
                        "To benchmark only latency and cost, since output quality can't be measured",
                        "To certify the model once before launch, after which prompts can change freely"),
                    Q(4, "What is prompt injection?",
                        "Prompt injection is untrusted content (user input, web pages, documents) containing instructions that try to override the application's intended behavior.",
                        correctAt: 2,
                        correct: "Untrusted input with instructions that try to override the app's behavior",
                        "Inserting user variables into a prompt template before it is sent",
                        "Sending many prompts in parallel to get around a provider's rate limits",
                        "Adding hidden examples to a prompt so the model copies their output format"),
                    Q(5, "What is the main benefit of streaming responses?",
                        "Streaming sends tokens to the client as they are generated, so users see output immediately. Total generation time is similar, but perceived latency drops a lot.",
                        correctAt: 1,
                        correct: "Tokens reach the client as they are generated, so users see output sooner",
                        "The full response is cached, so repeated questions are answered instantly",
                        "The model finishes faster because it skips its final self-review pass",
                        "Responses are compressed in transit, which lowers the cost of each request"),
                    Q(6, "Why split documents into chunks before embedding them for RAG?",
                        "Smaller, focused chunks produce more precise embeddings, improve retrieval relevance and let several results fit within the context window.",
                        correctAt: 2,
                        correct: "Focused chunks give more precise matches and fit within the context window",
                        "Embedding models accept only a single sentence and reject longer text",
                        "Each chunk is encrypted separately, so a leak exposes only part of a document",
                        "Smaller chunks remove the need for a vector database during retrieval"),
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
