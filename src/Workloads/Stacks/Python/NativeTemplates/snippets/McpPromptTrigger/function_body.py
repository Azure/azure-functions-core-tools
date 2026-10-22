@app.mcp_prompt_trigger(
    arg_name="context",
    prompt_name="code_review_checklist",
    description="Returns a structured code review checklist prompt for evaluating code changes."
)
def __FUNCTION_NAME__(context: func.PromptInvocationContext) -> str:
    logging.info("Code review checklist prompt invoked.")
    
    return """You are a senior software engineer performing a code review.
Use the following checklist to evaluate the code:

1. **Correctness** — Does the code do what it's supposed to?
2. **Error Handling** — Are edge cases and failures handled?
3. **Security** — Are there any vulnerabilities (injection, auth, secrets)?
4. **Performance** — Are there obvious inefficiencies?
5. **Readability** — Is the code clear and well-named?
6. **Tests** — Are there adequate tests for the changes?

Provide your feedback in a structured format with a severity level
(critical, warning, suggestion) for each finding."""

