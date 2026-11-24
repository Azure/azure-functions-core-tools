import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';

app.http('HttpFunction', {
    methods: ['GET', 'POST'],
    authLevel: 'function',
    handler: async (request: HttpRequest, context: InvocationContext): Promise<HttpResponseInit> => {
        context.log(`HTTP function processed request for url "${request.url}"`);

        const name = request.query.get('name') || await request.text() || 'world';

        return { body: `Hello, ${name}!` };
    }
});
