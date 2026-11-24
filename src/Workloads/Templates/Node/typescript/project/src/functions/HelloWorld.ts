import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';

app.http('HelloWorld', {
    methods: ['GET', 'POST'],
    authLevel: 'function',
    handler: async (request: HttpRequest, context: InvocationContext): Promise<HttpResponseInit> => {
        context.log(`HTTP function processed request for url "${request.url}"`);

        return { body: 'Hello, world!' };
    }
});
