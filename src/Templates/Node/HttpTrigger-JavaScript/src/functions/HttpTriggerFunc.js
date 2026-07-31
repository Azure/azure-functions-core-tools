const { app } = require('@azure/functions');

app.http('HttpTriggerFunc', {
    methods: ['GET', 'POST'],
    authLevel: 'AUTH_LEVEL_VALUE',
    handler: async (request, context) => {
        context.log(`Http function processed request for url "${request.url}"`);

        const name = request.query.get('name') || await request.text() || 'world';

        return { body: `Hello, ${name}!` };
    }
});
