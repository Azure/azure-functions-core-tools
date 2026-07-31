import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";

export async function HttpTriggerFunc(request: HttpRequest, context: InvocationContext): Promise<HttpResponseInit> {
    context.log(`Http function processed request for url "${request.url}"`);

    const name = request.query.get('name') || await request.text() || 'world';

    return { body: `Hello, ${name}!` };
};

app.http('HttpTriggerFunc', {
    methods: ['GET', 'POST'],
    authLevel: 'AUTH_LEVEL_VALUE',
    handler: HttpTriggerFunc
});
