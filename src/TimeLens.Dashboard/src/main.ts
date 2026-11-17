import './app.css';
import './fonts.css';
import './icons.css';
import App from './App.svelte';
import { mount } from 'svelte';

mount(App, { target: document.getElementById('app')! });
