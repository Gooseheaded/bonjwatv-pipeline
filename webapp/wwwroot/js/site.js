// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// ---------- Light/Dark Mode Toggle ----------
function applyTheme(theme) {
    document.documentElement.setAttribute('data-bs-theme', theme);
    const iconEl = document.getElementById('theme-icon');
    if (iconEl) {
        iconEl.textContent = theme === 'dark' ? '☀️' : '🌙';
    }
    localStorage.setItem('theme', theme);
}

function initThemeToggle() {
    const stored = localStorage.getItem('theme');
    const prefers = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    const initial = stored || prefers;
    applyTheme(initial);

    const btn = document.getElementById('theme-toggle');
    if (btn) {
        btn.addEventListener('click', () => {
            const current = document.documentElement.getAttribute('data-bs-theme');
            const next = current === 'dark' ? 'light' : 'dark';
            applyTheme(next);
        });
    }
}

document.addEventListener('DOMContentLoaded', initThemeToggle);
