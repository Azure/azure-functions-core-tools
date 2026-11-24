const { app } = require('@azure/functions');

app.http('HelloWorld', {
    methods: ['GET', 'POST'],
    authLevel: 'function',
    handler: async (request, context) => {
        context.log(`HTTP function processed request for url "${request.url}"`);

        return { body: 'Hello, world!' };
    }
});
