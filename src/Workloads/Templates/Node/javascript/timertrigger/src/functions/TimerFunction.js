const { app } = require('@azure/functions');

app.timer('TimerFunction', {
    schedule: '0 */5 * * * *',
    handler: (timer, context) => {
        context.log('Timer function processed request.');
    }
});
