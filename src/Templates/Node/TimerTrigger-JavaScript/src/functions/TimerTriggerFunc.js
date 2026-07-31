const { app } = require('@azure/functions');

app.timer('TimerTriggerFunc', {
    schedule: 'SCHEDULE_VALUE',
    handler: (myTimer, context) => {
        context.log('Timer function processed request.');
    }
});
