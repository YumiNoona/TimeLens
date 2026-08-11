import './app.css';
import '@fontsource/inter/latin-400.css';
import '@fontsource/inter/latin-500.css';
import '@fontsource/inter/latin-600.css';
import '@fontsource/jetbrains-mono/latin-400.css';
import '@fontsource/jetbrains-mono/latin-500.css';
import '@fontsource/jetbrains-mono/latin-600.css';
import '@tabler/icons-webfont/dist/tabler-icons.min.css';
import App from './App.svelte';
import { mount } from 'svelte';

mount(App, { target: document.getElementById('app')! });
