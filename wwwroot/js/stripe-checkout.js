// Marketplace buy-flow bridge (openspec: add-digital-asset-marketplace).
// The server creates a PaymentIntent; this confirms it in the browser with
// Stripe.js and resolves true/false. In development the fake payment gateway
// hands back a "pi_fake_*" client secret and Stripe.js isn't configured, so we
// short-circuit to a successful confirm — the server-side gateway verifies it.
window.stripeCheckout = {
    async confirm(publishableKey, clientSecret) {
        const looksFake = !clientSecret || clientSecret.startsWith("pi_fake");
        const configured = publishableKey && publishableKey.startsWith("pk_") && !publishableKey.includes("PLACEHOLDER");

        if (looksFake || !configured) {
            return true;
        }

        const stripe = await loadStripe(publishableKey);
        const { error } = await stripe.confirmPayment({
            clientSecret,
            confirmParams: { return_url: window.location.href },
            redirect: "if_required",
        });
        return !error;
    },
};

let _stripePromise = null;
function loadStripe(publishableKey) {
    if (_stripePromise) {
        return _stripePromise;
    }
    _stripePromise = new Promise((resolve, reject) => {
        if (window.Stripe) {
            resolve(window.Stripe(publishableKey));
            return;
        }
        const script = document.createElement("script");
        script.src = "https://js.stripe.com/v3/";
        script.onload = () => resolve(window.Stripe(publishableKey));
        script.onerror = reject;
        document.head.appendChild(script);
    });
    return _stripePromise;
}
