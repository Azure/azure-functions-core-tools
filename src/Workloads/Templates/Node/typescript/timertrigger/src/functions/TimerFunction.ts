import { app, InvocationContext, Timer } from '@azure/functions';

app.timer('TimerFunction', {
    schedule: '0 */5 * * * *',
    handler: (timer: Timer, context: InvocationContext): void => {
        context.log('Timer function processed request.');
    }
});
