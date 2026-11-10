class PulseReviewCarousel extends HTMLElement {
    connectedCallback() {
        queueMicrotask(() => {
            if (!this.isConnected || this.listeners) return;

            const rail = this.querySelector(".figma-review-list");
            const controls = this.querySelector(".figma-review-controls");
            const [previous, next] = controls.querySelectorAll("button");
            const updateControls = () => {
                const max = rail.scrollWidth - rail.clientWidth;
                controls.hidden = max <= 1;
                previous.disabled = rail.scrollLeft <= 1;
                next.disabled = rail.scrollLeft >= max - 1;
            };
            const move = (direction) => {
                const [first, second] = rail.children;
                if (!second) return;

                rail.scrollBy({ left: (second.offsetLeft - first.offsetLeft) * direction });
            };

            this.listeners = new AbortController();
            const options = { signal: this.listeners.signal };
            previous.addEventListener("click", () => move(-1), options);
            next.addEventListener("click", () => move(1), options);
            rail.addEventListener("scroll", updateControls, options);
            this.resize = new ResizeObserver(updateControls);
            this.resize.observe(rail);
            updateControls();
        });
    }

    disconnectedCallback() {
        this.listeners?.abort();
        this.resize?.disconnect();
        this.listeners = null;
    }
}

customElements.define("pulse-review-carousel", PulseReviewCarousel);
