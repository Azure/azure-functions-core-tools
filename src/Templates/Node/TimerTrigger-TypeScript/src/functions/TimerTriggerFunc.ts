import { app, InvocationContext, Timer } from "@azure/functions";

export async function TimerTriggerFunc(myTimer: Timer, context: InvocationContext): Promise<void> {
    context.log('Timer function processed request.');
}

app.timer('TimerTriggerFunc', {
    schedule: 'SCHEDULE_VALUE',
    handler: TimerTriggerFunc
});
