/**
 * Coalesces editor save bursts and guarantees at most one elaboration at a
 * time. A save received during an active pass schedules exactly one newest
 * follow-up pass.
 */
export class LatestReloadCoordinator {
    private timer: ReturnType<typeof setTimeout> | undefined;
    private running = false;
    private queued = false;
    private disposed = false;

    constructor(
        private readonly reload: () => Promise<void>,
        private readonly debounceMs: number
    ) { }

    schedule(): void {
        if (this.disposed) {
            return;
        }
        if (this.timer) {
            clearTimeout(this.timer);
        }
        this.timer = setTimeout(() => {
            this.timer = undefined;
            void this.requestNow();
        }, this.debounceMs);
    }

    async requestNow(): Promise<void> {
        if (this.disposed) {
            return;
        }
        if (this.timer) {
            clearTimeout(this.timer);
            this.timer = undefined;
        }
        if (this.running) {
            this.queued = true;
            return;
        }

        this.running = true;
        try {
            do {
                this.queued = false;
                await this.reload();
            } while (this.queued && !this.disposed);
        } finally {
            this.running = false;
        }
    }

    dispose(): void {
        this.disposed = true;
        this.queued = false;
        if (this.timer) {
            clearTimeout(this.timer);
            this.timer = undefined;
        }
    }
}
